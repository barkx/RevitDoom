// RevitDoom - IVideo implementation that renders Managed Doom's software
// renderer into a plain BGRA buffer the host (WPF WriteableBitmap) can blit.
// GPL-2.0-or-later (same as Managed Doom).
using System;
using System.Runtime.InteropServices;
using ManagedDoom;
using ManagedDoom.Video;

namespace RevitDoom
{
    public sealed class HostVideo : IVideo
    {
        private readonly Renderer renderer;
        private readonly byte[] columnMajorRgba;   // renderer output: x-major columns, RGBA

        public HostVideo(Config config, GameContent content)
        {
            renderer = new Renderer(config, content);
            columnMajorRgba = new byte[4 * renderer.Width * renderer.Height];
        }

        public int Width => renderer.Width;
        public int Height => renderer.Height;

        // IVideo.Render is unused by the engine loop itself; the host calls RenderTo.
        public void Render(Doom doom, Fixed frameFrac)
        {
            renderer.Render(doom, columnMajorRgba, frameFrac);
        }

        /// <summary>Render one frame and write it as row-major BGRX into dst.</summary>
        public unsafe void RenderTo(Doom doom, Fixed frameFrac, IntPtr dst, int dstStride)
        {
            renderer.Render(doom, columnMajorRgba, frameFrac);

            int w = renderer.Width, h = renderer.Height;
            fixed (byte* srcBytes = columnMajorRgba)
            {
                uint* src = (uint*)srcBytes;
                byte* dstRow = (byte*)dst;
                for (int y = 0; y < h; y++)
                {
                    uint* d = (uint*)(dstRow + (long)y * dstStride);
                    uint* s = src + y;
                    for (int x = 0; x < w; x++)
                    {
                        uint c = s[(long)x * h];                 // R | G<<8 | B<<16 | A<<24
                        d[x] = (c & 0xFF00FF00u) | ((c & 0xFFu) << 16) | ((c >> 16) & 0xFFu);
                    }
                }
            }
        }

        public void InitializeWipe() => renderer.InitializeWipe();
        public bool HasFocus() => true;

        public int MaxWindowSize => renderer.MaxWindowSize;
        public int WindowSize
        {
            get => renderer.WindowSize;
            set => renderer.WindowSize = value;
        }
        public bool DisplayMessage
        {
            get => renderer.DisplayMessage;
            set => renderer.DisplayMessage = value;
        }
        public int MaxGammaCorrectionLevel => renderer.MaxGammaCorrectionLevel;
        public int GammaCorrectionLevel
        {
            get => renderer.GammaCorrectionLevel;
            set => renderer.GammaCorrectionLevel = value;
        }
        public int WipeBandCount => renderer.WipeBandCount;
        public int WipeHeight => renderer.WipeHeight;
    }
}
