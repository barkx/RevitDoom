// RevitDoom - IUserInput fed by the host window (keyboard state + mouse deltas).
// Logic mirrors Managed Doom's SilkUserInput. GPL-2.0-or-later.
using System;
using System.Collections.Generic;
using ManagedDoom;
using ManagedDoom.UserInput;

namespace RevitDoom
{
    public sealed class HostInput : IUserInput
    {
        private readonly Config config;
        private readonly HashSet<DoomKey> pressed = new HashSet<DoomKey>();
        private readonly bool[] mouseButtons = new bool[5];
        private readonly bool[] weaponKeys = new bool[7];
        private int turnHeld;
        private float mouseDx, mouseDy;
        private bool mouseGrabbed;
        private readonly object sync = new object();

        public HostInput(Config config)
        {
            this.config = config;
        }

        // ---- fed by the host (UI thread)
        public void SetKey(DoomKey key, bool down)
        {
            lock (sync)
            {
                if (down) pressed.Add(key); else pressed.Remove(key);
            }
        }

        public void SetMouseButton(int index, bool down)
        {
            if (index >= 0 && index < mouseButtons.Length)
                lock (sync) mouseButtons[index] = down;
        }

        public void AddMouseDelta(float dx, float dy)
        {
            lock (sync)
            {
                mouseDx += dx;
                mouseDy += dy;
            }
        }

        public void ReleaseAll()
        {
            lock (sync)
            {
                pressed.Clear();
                Array.Clear(mouseButtons, 0, mouseButtons.Length);
                mouseDx = mouseDy = 0;
            }
        }

        /// <summary>True while the game wants the mouse (in a level, no menu).</summary>
        public bool MouseWanted => mouseGrabbed;

        // ---- IUserInput (engine)
        private bool IsPressed(KeyBinding binding)
        {
            foreach (var k in binding.Keys)
                if (pressed.Contains(k)) return true;
            foreach (var b in binding.MouseButtons)
            {
                int i = (int)b;
                if (i >= 0 && i < mouseButtons.Length && mouseButtons[i]) return true;
            }
            return false;
        }

        public void BuildTicCmd(TicCmd cmd)
        {
            float mdx, mdy;
            bool keyForward, keyBackward, keyStrafeLeft, keyStrafeRight, keyTurnLeft,
                 keyTurnRight, keyFire, keyUse, keyRun, keyStrafe;
            lock (sync)
            {
                keyForward = IsPressed(config.key_forward);
                keyBackward = IsPressed(config.key_backward);
                keyStrafeLeft = IsPressed(config.key_strafeleft);
                keyStrafeRight = IsPressed(config.key_straferight);
                keyTurnLeft = IsPressed(config.key_turnleft);
                keyTurnRight = IsPressed(config.key_turnright);
                keyFire = IsPressed(config.key_fire);
                keyUse = IsPressed(config.key_use);
                keyRun = IsPressed(config.key_run);
                keyStrafe = IsPressed(config.key_strafe);
                for (int i = 0; i < 7; i++)
                    weaponKeys[i] = pressed.Contains(DoomKey.Num1 + i);
                mdx = mouseDx; mdy = mouseDy;
                mouseDx = mouseDy = 0;
            }

            cmd.Clear();

            var strafe = keyStrafe;
            var speed = keyRun ? 1 : 0;
            var forward = 0;
            var side = 0;

            if (config.game_alwaysrun) speed = 1 - speed;

            if (keyTurnLeft || keyTurnRight) turnHeld++; else turnHeld = 0;
            int turnSpeed = turnHeld < PlayerBehavior.SlowTurnTics ? 2 : speed;

            if (strafe)
            {
                if (keyTurnRight) side += PlayerBehavior.SideMove[speed];
                if (keyTurnLeft) side -= PlayerBehavior.SideMove[speed];
            }
            else
            {
                if (keyTurnRight) cmd.AngleTurn -= (short)PlayerBehavior.AngleTurn[turnSpeed];
                if (keyTurnLeft) cmd.AngleTurn += (short)PlayerBehavior.AngleTurn[turnSpeed];
            }

            if (keyForward) forward += PlayerBehavior.ForwardMove[speed];
            if (keyBackward) forward -= PlayerBehavior.ForwardMove[speed];
            if (keyStrafeLeft) side -= PlayerBehavior.SideMove[speed];
            if (keyStrafeRight) side += PlayerBehavior.SideMove[speed];

            if (keyFire) cmd.Buttons |= TicCmdButtons.Attack;
            if (keyUse) cmd.Buttons |= TicCmdButtons.Use;

            for (var i = 0; i < weaponKeys.Length; i++)
            {
                if (weaponKeys[i])
                {
                    cmd.Buttons |= TicCmdButtons.Change;
                    cmd.Buttons |= (byte)(i << TicCmdButtons.WeaponShift);
                    break;
                }
            }

            if (mouseGrabbed)
            {
                if (config.mouse_disableyaxis) mdy = 0;
                var ms = 0.5F * config.mouse_sensitivity;
                var mx = (int)MathF.Round(ms * mdx);
                var my = (int)MathF.Round(ms * -mdy);
                forward += my;
                if (strafe) side += mx * 2;
                else cmd.AngleTurn -= (short)(mx * 0x8);
            }

            forward = Math.Clamp(forward, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);
            side = Math.Clamp(side, -PlayerBehavior.MaxMove, PlayerBehavior.MaxMove);

            cmd.ForwardMove += (sbyte)forward;
            cmd.SideMove += (sbyte)side;
        }

        public void Reset()
        {
            lock (sync) { mouseDx = mouseDy = 0; }
        }

        public void GrabMouse() { mouseGrabbed = true; }
        public void ReleaseMouse() { mouseGrabbed = false; }

        public int MaxMouseSensitivity => 15;
        public int MouseSensitivity
        {
            get => config.mouse_sensitivity;
            set => config.mouse_sensitivity = value;
        }
    }
}
