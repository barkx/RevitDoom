# -*- coding: utf-8 -*-
"""RevitDoom - DOOM inside Revit, powered by Managed Doom (pure C# / .NET 8).

Architecture (v4)
  * bin/RevitDoom.Managed.dll = Managed Doom engine (Nobuaki Tanaka, GPL-2)
    + a small host (DoomHost): software renderer -> BGRX buffer, keyboard /
    mouse input, and sound effects through Windows waveOut.
  * 100% managed code: no native DLL, no P/Invoke into the engine. Any
    engine failure is an ordinary .NET exception that the host reports in the
    console - Revit cannot be taken down by the game.
  * The engine is ticked at 35 Hz from WPF's render loop and rendered every
    display frame with interpolation (smooth 60 fps).
  * Built-in console (F12 or `) shows start-up steps, engine log and status.

Requires Revit 2025 or newer (.NET 8). pyRevit IronPython 2.7 / 3.4. GPL-2.0.
"""
import os
import sys
import time
import shutil

import clr
clr.AddReference('PresentationCore')
clr.AddReference('PresentationFramework')
clr.AddReference('WindowsBase')
clr.AddReference('System.Windows.Forms')
clr.AddReference('System.Drawing')

import System
from System.Windows import (Window, Thickness, Int32Rect, WindowStartupLocation,
                            GridLength, GridUnitType, Visibility, FontWeights)
from System.Windows.Controls import (Image, Grid, RowDefinition, Viewbox, TextBlock,
                                     TextBox, ScrollBarVisibility, DockPanel, Dock,
                                     Border)
from System.Windows.Input import Key, Keyboard, Mouse, Cursors, MouseButton
from System.Windows.Media import (CompositionTarget, Brushes, RenderOptions,
                                  BitmapScalingMode, PixelFormats, Stretch,
                                  FontFamily, SolidColorBrush, Color)
from System.Windows.Media.Imaging import WriteableBitmap
import System.Windows.Forms as WinForms
import System.Drawing as Drawing

EXT_DIR = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
BIN_DIR = os.path.join(EXT_DIR, 'bin')
ENGINE_DLL = os.path.join(BIN_DIR, 'RevitDoom.Managed.dll')
ENGINE_ASSEMBLY = 'RevitDoom.Managed'
WAD_DIR = os.path.join(EXT_DIR, 'wads')
DATA_DIR = os.path.join(os.environ.get('APPDATA') or os.path.expanduser('~'), 'RevitDoom')
RUN_DIR = os.path.join(os.environ.get('LOCALAPPDATA') or DATA_DIR, 'RevitDoom', 'run')
HOST_LOG = os.path.join(DATA_DIR, 'host.log')
ENGINE_LOG = os.path.join(DATA_DIR, 'doom.log')

API_VERSION = 4
CONSOLE_LINES = 400
CONSOLE_KEYS = (Key.F12, Key.Oem3)

IWAD_PREFERENCE = ['doom.wad', 'doomu.wad', 'doom2.wad', 'plutonia.wad',
                   'tnt.wad', 'doom1.wad', 'freedoom2.wad', 'freedoom1.wad']


# ---------------------------------------------------------------- keys
def _build_keymap():
    """WPF Key -> Managed Doom DoomKey name."""
    m = {}
    for i in range(26):
        c = chr(ord('A') + i)
        m[getattr(Key, c)] = c
    for i in range(10):
        m[getattr(Key, 'D%d' % i)] = 'Num%d' % i
        m[getattr(Key, 'NumPad%d' % i)] = 'Numpad%d' % i
    for i in range(1, 12):                     # F12 = console toggle
        m[getattr(Key, 'F%d' % i)] = 'F%d' % i
    m.update({
        Key.Up: 'Up', Key.Down: 'Down', Key.Left: 'Left', Key.Right: 'Right',
        Key.LeftCtrl: 'LControl', Key.RightCtrl: 'RControl',
        Key.LeftShift: 'LShift', Key.RightShift: 'RShift',
        Key.LeftAlt: 'LAlt', Key.RightAlt: 'RAlt',
        Key.Space: 'Space', Key.Enter: 'Enter', Key.Escape: 'Escape',
        Key.Tab: 'Tab', Key.Back: 'Backspace', Key.Pause: 'Pause',
        Key.PageUp: 'PageUp', Key.PageDown: 'PageDown', Key.Home: 'Home',
        Key.End: 'End', Key.Insert: 'Insert', Key.Delete: 'Delete',
        Key.OemComma: 'Comma', Key.OemPeriod: 'Period', Key.OemQuestion: 'Slash',
        Key.OemMinus: 'Subtract', Key.OemPlus: 'Equal',
        Key.Subtract: 'Subtract', Key.Add: 'Add',
        Key.Multiply: 'Multiply', Key.Divide: 'Divide',
        Key.OemSemicolon: 'Semicolon', Key.OemOpenBrackets: 'LBracket',
        Key.Oem6: 'RBracket', Key.Oem5: 'Backslash', Key.OemQuotes: 'Quote',
    })
    return m


KEYMAP = _build_keymap()


# ---------------------------------------------------------------- engine assembly
def load_engine(log):
    """Load RevitDoom.Managed once per Revit session (from a private copy so
    the extension folder stays updatable). Returns the DoomHost type."""
    engine_asm = None
    for asm in System.AppDomain.CurrentDomain.GetAssemblies():
        if asm.GetName().Name == ENGINE_ASSEMBLY:
            engine_asm = asm
            log('engine already loaded this session: %s (v%s)' % (asm.Location, asm.GetName().Version))
            break
    if engine_asm is None:
        if not os.path.isfile(ENGINE_DLL):
            raise IOError('Engine not found: %s' % ENGINE_DLL)
        if System.Environment.Version.Major < 8:
            raise RuntimeError('This Revit runs .NET %s; RevitDoom needs Revit 2025+ (.NET 8).'
                               % System.Environment.Version)
        run = os.path.join(RUN_DIR, '%d_%d' % (
            System.Diagnostics.Process.GetCurrentProcess().Id, int(time.time())))
        if not os.path.isdir(run):
            os.makedirs(run)
        dst = os.path.join(run, ENGINE_ASSEMBLY + '.dll')
        shutil.copyfile(ENGINE_DLL, dst)
        log('loading engine %s' % dst)
        engine_asm = System.Reflection.Assembly.LoadFrom(dst)
        clr.AddReference(engine_asm)
    # Resolve by reflection: "import RevitDoom" could collide with this
    # module's file name on a case-insensitive file system.
    t = engine_asm.GetType('RevitDoom.DoomHost', True)
    host_type = clr.GetPythonType(t)
    api = int(t.GetField('ApiVersion').GetValue(None))
    if api != API_VERSION:
        raise RuntimeError('Engine API v%s but script expects v%s. Restart Revit after updating.'
                           % (api, API_VERSION))
    return host_type


def find_iwads():
    found = []
    for d in (WAD_DIR, DATA_DIR):
        if os.path.isdir(d):
            for f in os.listdir(d):
                if f.lower().endswith('.wad'):
                    found.append(os.path.join(d, f))

    def rank(p):
        n = os.path.basename(p).lower()
        return IWAD_PREFERENCE.index(n) if n in IWAD_PREFERENCE else len(IWAD_PREFERENCE)
    return sorted(found, key=rank)


def _brush(r, g, b):
    return SolidColorBrush(Color.FromRgb(r, g, b))


# ---------------------------------------------------------------- window
class DoomWindow(Window):
    def __init__(self, iwad, title_suffix='', sound=True):
        self.iwad = iwad
        self.title_suffix = title_suffix
        self.want_sound = sound
        self.host = None
        self.final_state = None
        self.error_text = ''
        self.held = set()
        self.mouse_captured = False
        self.center = None
        self._lines = []
        self._last_poll = 0.0
        self._fps_t0 = time.time()
        self._fps_frames = 0
        self._fps = 0.0
        self._first_frame_at = None
        self._console_user_toggled = False
        self._render_handler = System.EventHandler(self._on_render)
        self._host_log = None
        try:
            if not os.path.isdir(DATA_DIR):
                os.makedirs(DATA_DIR)
            self._host_log = open(HOST_LOG, 'a')
        except Exception:
            pass
        self._build_ui()

    # ---- logging
    def log(self, msg):
        line = '%s [host]   %s' % (time.strftime('%H:%M:%S'), msg)
        self._console_add([line])
        if self._host_log:
            try:
                self._host_log.write(line + '\n')
                self._host_log.flush()
            except Exception:
                pass

    def _console_add(self, lines):
        if not lines:
            return
        self._lines.extend(lines)
        if len(self._lines) > CONSOLE_LINES:
            self._lines = self._lines[-CONSOLE_LINES:]
        self.console.Text = '\n'.join(self._lines)
        self.console.ScrollToEnd()

    def _pump_engine_log(self):
        if self.host is not None:
            lines = list(self.host.DrainLog())
            if lines:
                self._console_add(lines)
                if self._host_log:
                    try:
                        self._host_log.write('\n'.join(lines) + '\n')
                        self._host_log.flush()
                    except Exception:
                        pass

    # ---- UI
    def _build_ui(self):
        self.Title = 'DOOM' + (' - ' + self.title_suffix if self.title_suffix else '')
        self.Width = 1000
        self.Height = 860
        self.Background = Brushes.Black
        self.WindowStartupLocation = WindowStartupLocation.CenterScreen
        self.Focusable = True

        self.bitmap = WriteableBitmap(640, 400, 96, 96, PixelFormats.Bgr32, None)
        self.image = Image()
        self.image.Source = self.bitmap
        self.image.Width = 640
        self.image.Height = 480                 # Doom's 4:3 pixel aspect
        self.image.Stretch = Stretch.Fill
        RenderOptions.SetBitmapScalingMode(self.image, BitmapScalingMode.NearestNeighbor)
        view = Viewbox()
        view.Stretch = Stretch.Uniform
        view.Child = self.image

        mono = FontFamily('Consolas')
        self.status = TextBlock()
        self.status.FontFamily = mono
        self.status.FontWeight = FontWeights.Bold
        self.status.Foreground = _brush(120, 220, 120)
        self.status.Margin = Thickness(6, 3, 6, 3)
        self.console = TextBox()
        self.console.IsReadOnly = True
        self.console.Focusable = False
        self.console.IsTabStop = False
        self.console.FontFamily = mono
        self.console.FontSize = 11.5
        self.console.Background = _brush(12, 12, 12)
        self.console.Foreground = _brush(200, 200, 200)
        self.console.BorderThickness = Thickness(0)
        self.console.VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        self.console.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        help_line = TextBlock()
        help_line.FontFamily = mono
        help_line.FontSize = 11
        help_line.Foreground = Brushes.Gray
        help_line.Margin = Thickness(6, 2, 6, 3)
        help_line.Text = ('F12 / ` console   click = capture mouse (Esc frees)   WASD move   '
                          'arrows turn   Ctrl/LMB fire   Space/RMB use   Shift walk   Alt strafe   1-7 weapons   Tab map')
        cdock = DockPanel()
        DockPanel.SetDock(self.status, Dock.Top)
        DockPanel.SetDock(help_line, Dock.Bottom)
        cdock.Children.Add(self.status)
        cdock.Children.Add(help_line)
        cdock.Children.Add(self.console)
        self.console_panel = Border()
        self.console_panel.Background = _brush(12, 12, 12)
        self.console_panel.BorderBrush = _brush(60, 60, 60)
        self.console_panel.BorderThickness = Thickness(0, 1, 0, 0)
        self.console_panel.Child = cdock

        grid = Grid()
        r0 = RowDefinition()
        r0.Height = GridLength(1, GridUnitType.Star)
        r1 = RowDefinition()
        r1.Height = GridLength(220)
        grid.RowDefinitions.Add(r0)
        grid.RowDefinitions.Add(r1)
        self._console_row = r1
        Grid.SetRow(view, 0)
        Grid.SetRow(self.console_panel, 1)
        grid.Children.Add(view)
        grid.Children.Add(self.console_panel)
        self.Content = grid

        self.PreviewKeyDown += self._on_key_down
        self.PreviewKeyUp += self._on_key_up
        self.Deactivated += self._on_deactivated
        self.image.MouseDown += self._on_mouse_down
        self.image.MouseUp += self._on_mouse_up
        self.image.MouseMove += self._on_mouse_move
        self.ContentRendered += self._on_content_rendered
        self.Closing += self._on_closing

    def _set_console(self, visible):
        self.console_panel.Visibility = Visibility.Visible if visible else Visibility.Collapsed
        self._console_row.Height = GridLength(220) if visible else GridLength(0)

    def _toggle_console(self):
        self._console_user_toggled = True
        self._set_console(self.console_panel.Visibility != Visibility.Visible)

    # ---- lifecycle
    def _on_content_rendered(self, sender, e):
        # ContentRendered: the window (and console) are already on screen,
        # so the user sees progress while the WAD loads.
        self.log('=' * 60)
        self.log('RevitDoom host v%d | python %s | .NET %s | %d-bit' % (
            API_VERSION, sys.version.split()[0], System.Environment.Version,
            System.IntPtr.Size * 8))
        self.log('wad  %s' % self.iwad)
        self.log('data %s' % DATA_DIR)
        try:
            DoomHost = load_engine(self.log)
            self.host = DoomHost(self.iwad, DATA_DIR, self.want_sound)
            self.log('starting engine ...')
            ok = self.host.Start()
            self._pump_engine_log()
            if not ok:
                self._fail(self.host.Error)
                return
            self.log('engine running (sound: %s)' % ('on' if self.host.SoundEnabled else 'off'))
        except Exception as ex:
            self._fail('Host error: %s' % ex)
            return
        CompositionTarget.Rendering += self._render_handler
        self.Activate()
        Keyboard.Focus(self)

    def _on_closing(self, sender, e):
        try:
            CompositionTarget.Rendering -= self._render_handler
        except Exception:
            pass
        self._release_mouse()
        if self.host is not None:
            try:
                if self.final_state is None:
                    self.final_state = self.host.State
                self.host.Dispose()
                self._pump_engine_log()
            except Exception as ex:
                self.log('dispose error: %s' % ex)
        self.log('closed')
        if self._host_log:
            try:
                self._host_log.close()
            except Exception:
                pass

    def _fail(self, text):
        self.final_state = 'error'
        self.error_text = text
        self.log('ERROR: %s' % text)
        try:
            CompositionTarget.Rendering -= self._render_handler
        except Exception:
            pass
        self.Title = 'DOOM - stopped (Revit is fine)'
        self._console_user_toggled = True
        self._set_console(True)
        self._console_row.Height = GridLength(1, GridUnitType.Star)
        self.status.Foreground = _brush(255, 90, 90)
        self._console_add(['', '!!! ' + text,
                           '!!! logs: %s  and  %s' % (HOST_LOG, ENGINE_LOG),
                           '!!! close this window to return to Revit.'])
        self._update_status()

    # ---- per display frame
    def _on_render(self, sender, e):
        try:
            self._frame()
        except Exception as ex:
            self._fail('Host error in render loop: %s' % ex)

    def _frame(self):
        h = self.host
        now = time.time()
        h.Advance()
        st = h.State
        if st == 'quit':
            self.final_state = st
            CompositionTarget.Rendering -= self._render_handler
            self._pump_engine_log()
            self.log('player quit the game')
            self.Close()
            return
        if st == 'error':
            self._pump_engine_log()
            self._fail(h.Error)
            return

        self.bitmap.Lock()
        try:
            if h.RenderTo(self.bitmap.BackBuffer, self.bitmap.BackBufferStride):
                self.bitmap.AddDirtyRect(Int32Rect(0, 0, 640, 400))
                self._fps_frames += 1
        finally:
            self.bitmap.Unlock()

        if self._first_frame_at is None:
            self._first_frame_at = now
            self.log('first frame on screen')
        if not self._console_user_toggled and now - self._first_frame_at > 2.5:
            self._console_user_toggled = True
            self._set_console(False)

        if now - self._last_poll > 0.2:
            self._last_poll = now
            dt = now - self._fps_t0
            if dt >= 1.0:
                self._fps = self._fps_frames / dt
                self._fps_t0, self._fps_frames = now, 0
            self._pump_engine_log()
            self._update_status()

    def _update_status(self):
        h = self.host
        if h is None:
            self.status.Text = 'state: %s' % (self.final_state or 'no engine')
            return
        self.status.Text = 'state: %-8s fps: %5.1f  tics: %-7d frames: %-7d sound: %-3s mouse: %s   %s' % (
            self.final_state or h.State, self._fps, h.Tics, h.Frames,
            'on' if h.SoundEnabled else 'off',
            'captured' if self.mouse_captured else 'free', h.Stage)

    # ---- keyboard
    @staticmethod
    def _real_key(e):
        k = e.Key
        if k == Key.System:          # Alt and Alt+combos arrive as Key.System
            k = e.SystemKey
        return k

    def _running(self):
        return self.host is not None and self.final_state is None

    def _on_key_down(self, sender, e):
        k = self._real_key(e)
        if k == Key.F4 and (Keyboard.IsKeyDown(Key.LeftAlt) or Keyboard.IsKeyDown(Key.RightAlt)):
            return                   # let Alt+F4 close the window
        e.Handled = True
        if k in CONSOLE_KEYS:
            if not e.IsRepeat:
                self._toggle_console()
            return
        if k == Key.Escape:
            self._release_mouse()
        name = KEYMAP.get(k)
        if name is None or not self._running() or e.IsRepeat:
            return
        self.held.add(name)
        self.host.Key(name, True)

    def _on_key_up(self, sender, e):
        k = self._real_key(e)
        e.Handled = True
        name = KEYMAP.get(k)
        if name is None or not self._running():
            return
        self.held.discard(name)
        self.host.Key(name, False)

    def _on_deactivated(self, sender, e):
        self._release_mouse()
        if self._running():
            for name in list(self.held):
                self.host.Key(name, False)
            self.held.clear()
            self.host.ReleaseAll()

    # ---- mouse
    @staticmethod
    def _button_index(b):
        if b == MouseButton.Left:
            return 0
        if b == MouseButton.Right:
            return 1
        if b == MouseButton.Middle:
            return 2
        return -1

    def _capture_mouse(self):
        p = self.image.PointToScreen(System.Windows.Point(self.image.ActualWidth / 2,
                                                          self.image.ActualHeight / 2))
        self.center = Drawing.Point(int(p.X), int(p.Y))
        WinForms.Cursor.Position = self.center
        self.Cursor = getattr(Cursors, 'None')   # 'None' is a keyword in Py3
        Mouse.Capture(self.image)
        self.mouse_captured = True

    def _release_mouse(self):
        if self.mouse_captured:
            self.mouse_captured = False
            Mouse.Capture(None)
            self.Cursor = Cursors.Arrow
            if self._running():
                for i in range(3):
                    self.host.MouseButton(i, False)

    def _on_mouse_down(self, sender, e):
        e.Handled = True
        if not self._running():
            return
        if not self.mouse_captured:
            self._capture_mouse()
            return
        i = self._button_index(e.ChangedButton)
        if i >= 0:
            self.host.MouseButton(i, True)

    def _on_mouse_up(self, sender, e):
        if not self.mouse_captured:
            return
        e.Handled = True
        i = self._button_index(e.ChangedButton)
        if i >= 0 and self._running():
            self.host.MouseButton(i, False)

    def _on_mouse_move(self, sender, e):
        if not self.mouse_captured or not self._running():
            return
        pos = WinForms.Cursor.Position
        dx = pos.X - self.center.X
        dy = pos.Y - self.center.Y
        if dx or dy:
            self.host.MouseMove(dx, dy)
            WinForms.Cursor.Position = self.center


def play(iwad, title_suffix='', sound=True):
    """Open the game window modally. Returns (final_state, error_text)."""
    win = DoomWindow(iwad, title_suffix, sound)
    win.ShowDialog()
    return win.final_state, win.error_text
