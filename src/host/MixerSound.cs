// RevitDoom - sound effects for Managed Doom without OpenAL.
// A small software mixer (8 positional channels + 1 UI channel) whose output
// is streamed with the Win32 waveOut API (winmm.dll, present on every Windows).
// Channel management mirrors Managed Doom's SilkSound. GPL-2.0-or-later.
using System;
using System.Runtime.InteropServices;
using System.Threading;
using ManagedDoom;
using ManagedDoom.Audio;

namespace RevitDoom
{
    public sealed class MixerSound : ISound, IDisposable
    {
        public const int OutputRate = 44100;

        private const int ChannelCount = 8;
        private static readonly float FastDecay = (float)Math.Pow(0.5, 1.0 / (35 / 5));
        private static readonly float SlowDecay = (float)Math.Pow(0.5, 1.0 / 35);
        private const float ClipDist = 1200;
        private const float CloseDist = 160;
        private const float Attenuator = ClipDist - CloseDist;

        private readonly Config config;
        private readonly Clip[] clips;
        private readonly float[] amplitudes;
        private readonly DoomRandom random;
        private readonly Voice[] voices = new Voice[ChannelCount + 1]; // last = UI
        private readonly ChannelInfo[] infos = new ChannelInfo[ChannelCount];
        private Sfx uiReserved = Sfx.NONE;
        private Mobj listener;
        private float masterVolume;
        private readonly object mixLock = new object();
        private WaveOutPlayer player;

        private sealed class Clip
        {
            public float[] Samples;   // -1..1
            public int Rate;
        }

        private sealed class Voice
        {
            public Clip Clip;
            public double Pos;
            public double Step;
            public float Left, Right;   // gains
            public bool Playing;
            public bool Paused;
        }

        private sealed class ChannelInfo
        {
            public Sfx Reserved = Sfx.NONE;
            public Sfx Playing = Sfx.NONE;
            public float Priority;
            public Mobj Source;
            public SfxType Type;
            public int Volume;
            public Fixed LastX, LastY;
            public void Clear()
            {
                Reserved = Sfx.NONE; Playing = Sfx.NONE; Priority = 0; Source = null;
                Type = 0; Volume = 0; LastX = Fixed.Zero; LastY = Fixed.Zero;
            }
        }

        /// <param name="startDevice">false = mix only (tests); true = open waveOut.</param>
        public MixerSound(Config config, GameContent content, bool startDevice)
        {
            this.config = config;
            config.audio_soundvolume = Math.Clamp(config.audio_soundvolume, 0, MaxVolume);
            masterVolume = (float)config.audio_soundvolume / MaxVolume;
            if (config.audio_randompitch) random = new DoomRandom();

            clips = new Clip[DoomInfo.SfxNames.Length];
            amplitudes = new float[DoomInfo.SfxNames.Length];
            for (var i = 0; i < DoomInfo.SfxNames.Length; i++)
            {
                var name = "DS" + DoomInfo.SfxNames[i].ToString().ToUpper();
                if (content.Wad.GetLumpNumber(name) == -1) continue;
                var clip = LoadClip(content.Wad, name, out var amp);
                if (clip != null)
                {
                    clips[i] = clip;
                    amplitudes[i] = amp;
                }
            }
            for (int i = 0; i < voices.Length; i++) voices[i] = new Voice();
            for (int i = 0; i < infos.Length; i++) infos[i] = new ChannelInfo();

            if (startDevice)
                player = new WaveOutPlayer(OutputRate, Mix);
        }

        private static Clip LoadClip(Wad wad, string name, out float amplitude)
        {
            amplitude = 0;
            var data = wad.ReadLump(name);
            if (data.Length < 8) return null;
            int rate = BitConverter.ToUInt16(data, 2);
            int count = BitConverter.ToInt32(data, 4);
            int offset = 8;
            if (ContainsDmxPadding(data)) { offset += 16; count -= 32; }
            count = Math.Min(count, data.Length - offset);
            if (count <= 0 || rate <= 0) return null;
            var s = new float[count];
            int max = 0;
            int ampCount = Math.Min(rate / 5, count);
            for (int i = 0; i < count; i++)
            {
                int v = data[offset + i] - 128;
                s[i] = v / 128f;
                if (i < ampCount && Math.Abs(v) > max) max = Math.Abs(v);
            }
            amplitude = max / 128f;
            return new Clip { Samples = s, Rate = rate };
        }

        private static bool ContainsDmxPadding(byte[] data)
        {
            var n = BitConverter.ToInt32(data, 4);
            if (n < 32 || data.Length < 8 + n) return false;
            var first = data[8];
            for (var i = 1; i < 16; i++) if (data[8 + i] != first) return false;
            var last = data[8 + n - 1];
            for (var i = 1; i < 16; i++) if (data[8 + n - i - 1] != last) return false;
            return true;
        }

        // ---------------------------------------------------------------- mixer
        /// <summary>Mix interleaved stereo 16-bit frames (called from the audio thread).</summary>
        public void Mix(short[] buffer, int frames)
        {
            lock (mixLock)
            {
                for (int f = 0; f < frames; f++)
                {
                    float l = 0, r = 0;
                    for (int v = 0; v < voices.Length; v++)
                    {
                        var vo = voices[v];
                        if (!vo.Playing || vo.Paused) continue;
                        var smp = vo.Clip.Samples;
                        int i = (int)vo.Pos;
                        if (i >= smp.Length - 1)
                        {
                            vo.Playing = false;
                            continue;
                        }
                        float frac = (float)(vo.Pos - i);
                        float s = smp[i] + (smp[i + 1] - smp[i]) * frac;
                        l += s * vo.Left;
                        r += s * vo.Right;
                        vo.Pos += vo.Step;
                    }
                    buffer[2 * f] = ToShort(l);
                    buffer[2 * f + 1] = ToShort(r);
                }
            }
        }

        private static short ToShort(float x)
        {
            x *= 0.6f * 32767f;
            if (x > 32767f) x = 32767f;
            if (x < -32768f) x = -32768f;
            return (short)x;
        }

        private void PlayVoice(Voice v, Clip c, float pitch)
        {
            v.Clip = c;
            v.Pos = 0;
            v.Step = (double)c.Rate * pitch / OutputRate;
            v.Paused = false;
            v.Playing = true;
        }

        private static void SetGain(Voice v, float volume, float pan /* -1 left .. 1 right */)
        {
            v.Left = volume * Math.Min(1f, 1f - pan);
            v.Right = volume * Math.Min(1f, 1f + pan);
        }

        // ---------------------------------------------------------------- ISound
        public void SetListener(Mobj listener) => this.listener = listener;

        public void Update()
        {
            lock (mixLock)
            {
                for (var i = 0; i < infos.Length; i++)
                {
                    var info = infos[i];
                    var voice = voices[i];
                    if (info.Playing != Sfx.NONE)
                    {
                        if (voice.Playing)
                        {
                            info.Priority *= info.Type == SfxType.Diffuse ? SlowDecay : FastDecay;
                            SetParam(voice, info);
                        }
                        else
                        {
                            info.Playing = Sfx.NONE;
                            if (info.Reserved == Sfx.NONE) info.Source = null;
                        }
                    }
                    if (info.Reserved != Sfx.NONE)
                    {
                        var clip = clips[(int)info.Reserved];
                        if (clip != null)
                        {
                            SetParam(voice, info);
                            PlayVoice(voice, clip, GetPitch(info.Type, info.Reserved));
                            info.Playing = info.Reserved;
                        }
                        info.Reserved = Sfx.NONE;
                    }
                }

                if (uiReserved != Sfx.NONE)
                {
                    var ui = voices[ChannelCount];
                    var clip = clips[(int)uiReserved];
                    if (clip != null)
                    {
                        SetGain(ui, masterVolume, 0);
                        PlayVoice(ui, clip, 1f);
                    }
                    uiReserved = Sfx.NONE;
                }
            }
        }

        public void StartSound(Sfx sfx)
        {
            if (clips[(int)sfx] == null) return;
            uiReserved = sfx;
        }

        public void StartSound(Mobj mobj, Sfx sfx, SfxType type) => StartSound(mobj, sfx, type, 100);

        public void StartSound(Mobj mobj, Sfx sfx, SfxType type, int volume)
        {
            if (clips[(int)sfx] == null || listener == null) return;

            var x = (mobj.X - listener.X).ToFloat();
            var y = (mobj.Y - listener.Y).ToFloat();
            var dist = MathF.Sqrt(x * x + y * y);
            float priority = type == SfxType.Diffuse ? volume : amplitudes[(int)sfx] * GetDistanceDecay(dist) * volume;

            foreach (var info in infos)
            {
                if (info.Source == mobj && info.Type == type)
                {
                    info.Reserved = sfx; info.Priority = priority; info.Volume = volume;
                    return;
                }
            }
            foreach (var info in infos)
            {
                if (info.Reserved == Sfx.NONE && info.Playing == Sfx.NONE)
                {
                    info.Reserved = sfx; info.Priority = priority; info.Source = mobj;
                    info.Type = type; info.Volume = volume;
                    return;
                }
            }
            var minPriority = float.MaxValue;
            var minChannel = -1;
            for (var i = 0; i < infos.Length; i++)
            {
                if (infos[i].Priority < minPriority) { minPriority = infos[i].Priority; minChannel = i; }
            }
            if (minChannel >= 0 && priority >= minPriority)
            {
                var info = infos[minChannel];
                info.Reserved = sfx; info.Priority = priority; info.Source = mobj;
                info.Type = type; info.Volume = volume;
            }
        }

        public void StopSound(Mobj mobj)
        {
            foreach (var info in infos)
            {
                if (info.Source == mobj)
                {
                    info.LastX = info.Source.X;
                    info.LastY = info.Source.Y;
                    info.Source = null;
                    info.Volume /= 5;
                }
            }
        }

        public void Reset()
        {
            random?.Clear();
            lock (mixLock)
            {
                for (var i = 0; i < infos.Length; i++)
                {
                    voices[i].Playing = false;
                    infos[i].Clear();
                }
            }
            listener = null;
        }

        public void Pause()
        {
            lock (mixLock)
            {
                for (int i = 0; i < ChannelCount; i++)
                {
                    var v = voices[i];
                    if (v.Playing && (v.Clip.Samples.Length - v.Pos) / v.Clip.Rate > 0.2)
                        v.Paused = true;
                }
            }
        }

        public void Resume()
        {
            lock (mixLock)
            {
                foreach (var v in voices) v.Paused = false;
            }
        }

        private void SetParam(Voice v, ChannelInfo info)
        {
            if (info.Type == SfxType.Diffuse || listener == null)
            {
                SetGain(v, 0.01F * masterVolume * info.Volume, 0);
                return;
            }
            Fixed sx, sy;
            if (info.Source == null) { sx = info.LastX; sy = info.LastY; }
            else { sx = info.Source.X; sy = info.Source.Y; }
            var x = (sx - listener.X).ToFloat();
            var y = (sy - listener.Y).ToFloat();
            if (Math.Abs(x) < 16 && Math.Abs(y) < 16)
            {
                SetGain(v, 0.01F * masterVolume * info.Volume, 0);
            }
            else
            {
                var dist = MathF.Sqrt(x * x + y * y);
                var angle = MathF.Atan2(y, x) - (float)listener.Angle.ToRadian();
                var pan = -MathF.Sin(angle) * 0.75f;
                SetGain(v, 0.01F * masterVolume * GetDistanceDecay(dist) * info.Volume, pan);
            }
        }

        private static float GetDistanceDecay(float dist)
        {
            if (dist < CloseDist) return 1F;
            return Math.Max((ClipDist - dist) / Attenuator, 0F);
        }

        private float GetPitch(SfxType type, Sfx sfx)
        {
            if (random == null) return 1f;
            if (sfx == Sfx.ITEMUP || sfx == Sfx.TINK || sfx == Sfx.RADIO) return 1f;
            if (type == SfxType.Voice) return 1.0F + 0.075F * (random.Next() - 128) / 128;
            return 1.0F + 0.025F * (random.Next() - 128) / 128;
        }

        public int MaxVolume => 15;
        public int Volume
        {
            get => config.audio_soundvolume;
            set
            {
                config.audio_soundvolume = value;
                masterVolume = (float)value / MaxVolume;
            }
        }

        public void Dispose()
        {
            player?.Dispose();
            player = null;
        }
    }

    /// <summary>Streams PCM produced by a callback through winmm waveOut (polling thread).</summary>
    internal sealed class WaveOutPlayer : IDisposable
    {
        private const int BufferCount = 4;
        private const int FramesPerBuffer = 1024;   // ~23 ms at 44.1 kHz

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEFORMATEX
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WAVEHDR
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        private const uint WHDR_DONE = 0x1;
        [DllImport("winmm.dll")] private static extern int waveOutOpen(out IntPtr h, uint dev, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] private static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveOutUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] private static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] private static extern int waveOutClose(IntPtr h);

        private readonly Action<short[], int> fill;
        private IntPtr hwo;
        private readonly IntPtr[] hdrs = new IntPtr[BufferCount];
        private readonly IntPtr[] data = new IntPtr[BufferCount];
        private readonly short[] scratch = new short[FramesPerBuffer * 2];
        private readonly Thread thread;
        private volatile bool running = true;
        private static readonly int HdrSize = Marshal.SizeOf<WAVEHDR>();

        public WaveOutPlayer(int rate, Action<short[], int> fill)
        {
            this.fill = fill;
            var fmt = new WAVEFORMATEX
            {
                wFormatTag = 1, nChannels = 2, nSamplesPerSec = (uint)rate,
                wBitsPerSample = 16, nBlockAlign = 4, nAvgBytesPerSec = (uint)rate * 4, cbSize = 0
            };
            int r = waveOutOpen(out hwo, 0xFFFFFFFF /* WAVE_MAPPER */, ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
            if (r != 0) throw new InvalidOperationException("waveOutOpen failed: " + r);

            for (int i = 0; i < BufferCount; i++)
            {
                data[i] = Marshal.AllocHGlobal(FramesPerBuffer * 4);
                hdrs[i] = Marshal.AllocHGlobal(HdrSize);
                var h = new WAVEHDR { lpData = data[i], dwBufferLength = (uint)(FramesPerBuffer * 4) };
                Marshal.StructureToPtr(h, hdrs[i], false);
                waveOutPrepareHeader(hwo, hdrs[i], HdrSize);
                Submit(i);
            }
            thread = new Thread(Loop) { IsBackground = true, Name = "RevitDoom audio", Priority = ThreadPriority.AboveNormal };
            thread.Start();
        }

        private void Submit(int i)
        {
            fill(scratch, FramesPerBuffer);
            Marshal.Copy(scratch, 0, data[i], scratch.Length);
            // clear DONE flag (offset of dwFlags)
            int flagsOffset = (int)Marshal.OffsetOf<WAVEHDR>(nameof(WAVEHDR.dwFlags));
            uint flags = (uint)Marshal.ReadInt32(hdrs[i], flagsOffset);
            Marshal.WriteInt32(hdrs[i], flagsOffset, (int)(flags & ~WHDR_DONE));
            waveOutWrite(hwo, hdrs[i], HdrSize);
        }

        private void Loop()
        {
            int flagsOffset = (int)Marshal.OffsetOf<WAVEHDR>(nameof(WAVEHDR.dwFlags));
            while (running)
            {
                bool any = false;
                for (int i = 0; i < BufferCount && running; i++)
                {
                    uint flags = (uint)Marshal.ReadInt32(hdrs[i], flagsOffset);
                    if ((flags & WHDR_DONE) != 0)
                    {
                        try { Submit(i); } catch { }
                        any = true;
                    }
                }
                if (!any) Thread.Sleep(3);
            }
        }

        public void Dispose()
        {
            running = false;
            thread?.Join(500);
            if (hwo != IntPtr.Zero)
            {
                waveOutReset(hwo);
                for (int i = 0; i < BufferCount; i++)
                {
                    waveOutUnprepareHeader(hwo, hdrs[i], HdrSize);
                    Marshal.FreeHGlobal(hdrs[i]);
                    Marshal.FreeHGlobal(data[i]);
                }
                waveOutClose(hwo);
                hwo = IntPtr.Zero;
            }
        }
    }
}
