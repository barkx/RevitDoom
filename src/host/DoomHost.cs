// RevitDoom - host facade around Managed Doom (C#) for use from pyRevit.
//
// 100% managed: the engine runs on the caller's thread (Revit's UI thread via
// the WPF render loop), every failure is a normal .NET exception that the
// host catches and reports, and nothing can take the Revit process down.
//
//   var host = new DoomHost(wadPath, dataDir, sound: true);
//   host.Start();                       // loads WAD, builds renderer, game
//   each UI frame:  host.Advance();     // runs due 35 Hz tics
//                   host.RenderTo(backBuffer, stride);
//   input:          host.Key("W", true) / host.MouseMove(dx, dy) / host.MouseButton(0, true)
//
// GPL-2.0-or-later (same as Managed Doom).
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using ManagedDoom;
using ManagedDoom.Audio;
using ManagedDoom.UserInput;

namespace RevitDoom
{
    public sealed class DoomHost : IDisposable
    {
        public const int ApiVersion = 4;
        public const double TicRate = 35.0;

        private readonly string wadPath;
        private readonly string dataDir;
        private readonly bool wantSound;
        private readonly string[] extraArgs;

        private Config config;
        private GameContent content;
        private HostVideo video;
        private HostInput input;
        private MixerSound sound;
        private Doom doom;

        private readonly Stopwatch clock = new Stopwatch();
        private long ticsRun;
        private long framesRendered;
        private double nextTicTime;
        private readonly ConcurrentQueue<string> log = new ConcurrentQueue<string>();

        public DoomHost(string wadPath, string dataDir, bool sound)
            : this(wadPath, dataDir, sound, new string[0]) { }

        public DoomHost(string wadPath, string dataDir, bool sound, string[] extraArgs)
        {
            this.wadPath = wadPath;
            this.dataDir = dataDir;
            this.wantSound = sound;
            this.extraArgs = extraArgs ?? new string[0];
            State = "created";
        }

        // ------------------------------------------------------------ status
        public string State { get; private set; }          // created/starting/running/quit/error
        public bool Running => State == "running";
        public string Error { get; private set; } = "";
        public string QuitMessage => doom?.QuitMessage ?? "";
        public long Tics => ticsRun;
        public long Frames => framesRendered;
        public int Width => video?.Width ?? 640;
        public int Height => video?.Height ?? 400;
        public bool SoundEnabled => sound != null;
        /// <summary>True when the game wants relative mouse input (in a level, menu closed).</summary>
        public bool MouseWanted => input != null && input.MouseWanted;
        public string Stage { get; private set; } = "";

        public void Log(string msg)
        {
            log.Enqueue(string.Format("{0:HH:mm:ss} [engine] {1}", DateTime.Now, msg));
            try
            {
                if (dataDir != null)
                    File.AppendAllText(Path.Combine(dataDir, "doom.log"),
                        string.Format("{0:HH:mm:ss.fff} {1}{2}", DateTime.Now, msg, Environment.NewLine));
            }
            catch { }
        }

        /// <summary>Returns and removes queued log lines (for the host console).</summary>
        public string[] DrainLog()
        {
            var list = new System.Collections.Generic.List<string>();
            while (log.TryDequeue(out var s)) list.Add(s);
            return list.ToArray();
        }

        // ------------------------------------------------------------ lifecycle
        public bool Start()
        {
            State = "starting";
            var oldOut = Console.Out;
            var capture = new LineCapture(this);
            try
            {
                if (dataDir != null)
                {
                    Directory.CreateDirectory(dataDir);
                    try { File.WriteAllText(Path.Combine(dataDir, "doom.log"), ""); } catch { }
                }
                ConfigUtilities.DataDirectory = dataDir;
                Log("RevitDoom managed engine API v" + ApiVersion + " on .NET " + Environment.Version);
                Console.SetOut(capture);   // Managed Doom reports init steps on Console

                Step("command line");
                var args = new System.Collections.Generic.List<string> { "-iwad", wadPath };
                args.AddRange(extraArgs);
                var cmd = new CommandLineArgs(args.ToArray());

                Step("config");
                var cfgPath = ConfigUtilities.GetConfigPath();
                config = File.Exists(cfgPath) ? new Config(cfgPath) : new Config();
                config.video_highresolution = true;
                config.mouse_disableyaxis = true;   // mouse turns only; no "mouse walks forward"

                Step("loading WAD " + wadPath);
                content = new GameContent(cmd);

                Step("renderer");
                video = new HostVideo(config, content);

                Step("input");
                input = new HostInput(config);

                if (wantSound)
                {
                    Step("sound (waveOut)");
                    try
                    {
                        sound = new MixerSound(config, content, startDevice: true);
                    }
                    catch (Exception ex)
                    {
                        sound = null;
                        Log("sound disabled: " + ex.Message);
                    }
                }

                Step("game");
                doom = new Doom(cmd, config, content, video, sound, null, input);

                clock.Restart();
                nextTicTime = 0;
                State = "running";
                Step("running");
                return true;
            }
            catch (Exception ex)
            {
                Fail("start (" + Stage + ")", ex);
                return false;
            }
            finally
            {
                capture.Flush();
                Console.SetOut(oldOut);
            }
        }

        private void Step(string s)
        {
            Stage = s;
            Log("stage: " + s);
        }

        private void Fail(string where, Exception ex)
        {
            State = "error";
            Error = where + ": " + ex.GetType().Name + ": " + ex.Message;
            Log("ERROR " + Error);
            Log(ex.ToString());
        }

        /// <summary>Run all 35 Hz tics that are due. Returns number of tics run.</summary>
        public int Advance()
        {
            if (!Running) return 0;
            int n = 0;
            try
            {
                double now = clock.Elapsed.TotalSeconds;
                // don't try to catch up more than ~0.25 s (e.g. after a modal dialog)
                if (now - nextTicTime > 0.25) nextTicTime = now - 1.0 / TicRate;
                while (nextTicTime <= now && n < 10)
                {
                    Stage = "tic " + ticsRun;
                    if (doom.Update() == UpdateResult.Completed)
                    {
                        State = "quit";
                        Log("quit: " + QuitMessage);
                        return n;
                    }
                    ticsRun++;
                    n++;
                    nextTicTime += 1.0 / TicRate;
                }
            }
            catch (Exception ex)
            {
                Fail("tic " + ticsRun, ex);
            }
            return n;
        }

        /// <summary>Render the current state into a BGRX buffer (row-major).</summary>
        public bool RenderTo(IntPtr dst, int stride)
        {
            if (doom == null || video == null) return false;
            if (State != "running" && State != "quit") return false;
            try
            {
                // interpolate between the last two tics for smooth motion
                double since = clock.Elapsed.TotalSeconds - (nextTicTime - 1.0 / TicRate);
                double frac = Math.Clamp(since * TicRate, 0.05, 1.0);
                video.RenderTo(doom, Fixed.FromDouble(frac), dst, stride);
                framesRendered++;
                return true;
            }
            catch (Exception ex)
            {
                Fail("render", ex);
                return false;
            }
        }

        // ------------------------------------------------------------ input
        /// <summary>Key by DoomKey name, e.g. "W", "LControl", "Escape", "Num1", "F2".</summary>
        public bool Key(string doomKeyName, bool down)
        {
            if (!Enum.TryParse(doomKeyName, out DoomKey key)) return false;
            KeyCode(key, down);
            return true;
        }

        public void KeyCode(DoomKey key, bool down)
        {
            if (doom == null || !Running) return;
            input.SetKey(key, down);
            doom.PostEvent(new DoomEvent(down ? EventType.KeyDown : EventType.KeyUp, key));
        }

        public void MouseMove(double dx, double dy)
        {
            input?.AddMouseDelta((float)dx, (float)dy);
        }

        /// <summary>button: 0 left, 1 right, 2 middle.</summary>
        public void MouseButton(int button, bool down)
        {
            input?.SetMouseButton(button, down);
        }

        public void ReleaseAll()
        {
            input?.ReleaseAll();
        }

        public void Dispose()
        {
            try
            {
                if (config != null)
                    config.Save(ConfigUtilities.GetConfigPath());
            }
            catch (Exception ex) { Log("config save failed: " + ex.Message); }
            try { sound?.Dispose(); } catch { }
            sound = null;
            try { content?.Dispose(); } catch { }
            content = null;
            if (State == "running") State = "quit";
            Log("disposed after " + ticsRun + " tics, " + framesRendered + " frames");
        }

        /// <summary>Collects Console output from Managed Doom into the host log.</summary>
        private sealed class LineCapture : TextWriter
        {
            private readonly DoomHost host;
            private readonly StringBuilder sb = new StringBuilder();
            public LineCapture(DoomHost host) { this.host = host; }
            public override Encoding Encoding => Encoding.UTF8;
            public override void Write(char value)
            {
                if (value == '\n') Flush();
                else if (value != '\r') sb.Append(value);
            }
            public override void Flush()
            {
                if (sb.Length > 0) { host.Log(sb.ToString()); sb.Clear(); }
            }
        }
    }
}
