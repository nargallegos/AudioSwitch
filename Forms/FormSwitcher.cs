﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AudioSwitch.Classes;
using AudioSwitch.CoreAudioApi;
using AudioSwitch.Properties;

namespace AudioSwitch.Forms
{
    public partial class FormSwitcher : Form
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int SetWindowTheme(IntPtr hWnd, string appName, string partList);

        private const int WM_HOTKEY = 0x0312;
        private readonly List<Icon> DefaultTrayIcons = new List<Icon>();
        private List<Icon> ActiveTrayIcons = new List<Icon>();
        private static EDataFlow RenderType;
        internal static float DpiFactor;

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_HOTKEY:
                    HotKeyPressed((short)m.WParam);
                    break;

                case 0x84:
                    break;

                default:
                    base.WndProc(ref m);
                    break;
            }
        }

        public FormSwitcher()
        {
            InitializeComponent();
            ledLeft.OldStyle = Program.settings.ColorVU;
            ledLeft.SetValue((float)0.1);
            ledRight.OldStyle = Program.settings.ColorVU;
            ledRight.SetValue((float)0.1);

            SetWindowTheme(listDevices.Handle, "explorer", null);
            
            using (var gr = CreateGraphics())
                DpiFactor = gr.DpiX / 96;
            var tile = new Size(listDevices.ClientSize.Width + 2, (int)(listDevices.TileSize.Height * DpiFactor));

            DeviceIcons.InitImageLists(DpiFactor);

            listDevices.TileSize = tile;
            listDevices.Scroll += VolBar.DoScroll;
            listDevices.LargeImageList = DeviceIcons.ActiveIcons;

            if (DpiFactor <= 1)
            {
                DefaultTrayIcons.Add(getIcon(Resources.mute));
                DefaultTrayIcons.Add(getIcon(Resources.zero));
                DefaultTrayIcons.Add(getIcon(Resources._1_33));
                DefaultTrayIcons.Add(getIcon(Resources._33_66));
                DefaultTrayIcons.Add(getIcon(Resources._66_100));
            }
            else
            {
                DefaultTrayIcons.Add(getIcon(Resources.mute_highDPI));
                DefaultTrayIcons.Add(getIcon(Resources.zero_highDPI));
                DefaultTrayIcons.Add(getIcon(Resources._1_33_highDPI));
                DefaultTrayIcons.Add(getIcon(Resources._33_66_highDPI));
                DefaultTrayIcons.Add(getIcon(Resources._66_100_highDPI));
            }

            RenderType = Program.settings.DefaultDataFlow;
            RefreshDevices(RenderType);
            SetTrayIcons();

            VolBar.VolumeMuteChanged += IconChanged;
            VolBar.RegisterDevice(RenderType);
            EndPoints.NotifyClient.DefaultChanged += DefaultChanged;
            EndPoints.NotifyClient.DeviceAdded += DeviceAdded;
            EndPoints.NotifyClient.DeviceRemoved += DeviceRemoved;
            
            GlobalHotkeys.Handle = Handle;
            GlobalHotkeys.RegisterAll();

            ScrollVolume.VolumeScroll += ScrollTheVolume;
            ScrollVolume.RegisterVolScroll(Program.settings.VolumeScroll.Enabled);
        }

        private void DefaultChanged(string devID)
        {
            if (InvokeRequired)
                BeginInvoke(new DevEventDelegate(DefaultChanged), devID);
            else
            {
                foreach (ListViewItem item in listDevices.Items)
                {
                    if ((string) item.Tag == devID)
                    {
                        item.Selected = true;
                        SetTrayIcons();
                        VolBar.RegisterDevice(RenderType);
                    }
                    SetDeviceIcon(item.Index, item.Selected);
                }
            }
        }

        private void DeviceAdded(string devID)
        {
            if (InvokeRequired)
                BeginInvoke(new DevEventDelegate(DeviceAdded), devID);
            else
            {
                if (listDevices.Items.Cast<ListViewItem>().Any(item => (string) item.Tag == devID))
                    return;

                var pDevices = EndPoints.DeviceEnumerator.EnumerateAudioEndPoints(RenderType, EDeviceState.Active);
                if (pDevices.Count > 0)
                {
                    for (var i = 0; i < pDevices.Count; i++)
                    {
                        if (pDevices[i].ID == devID)
                        {
                            if (listDevices.Items.Cast<ListViewItem>().Any(item => (string) item.Tag == devID))
                                return;

                            var device = pDevices[i];
                            DeviceIcons.Add(device.IconPath);
                            var devSettings = Program.settings.Device.Find(x => x.DeviceID == devID);
                            if (devSettings == null || !devSettings.HideFromList)
                            {
                                var item = new ListViewItem
                                {
                                    ImageIndex = listDevices.Items.Count,
                                    Text = device.FriendlyName,
                                    Tag = devID,
                                };

                                listDevices.Items.Add(item);
                            }
                            listDevices.Sort();
                            SetSizes();
                        }
                    }
                }
            }
        }

        private void DeviceRemoved(string devID)
        {
            if (InvokeRequired)
                BeginInvoke(new DevEventDelegate(DeviceRemoved), devID);
            else
            {
                foreach (var item in listDevices.Items.Cast<ListViewItem>().Where(item => (string) item.Tag == devID))
                {
                    DeviceIcons.Remove(item.Index);
                    listDevices.Items.Remove(item);
                    listDevices.Sort();
                    SetSizes();
                    break;
                }
            }
        }

        private static Icon getIcon(Bitmap source)
        {
            return Icon.FromHandle(source.GetHicon());
        }

        private void FormSwitcher_FormClosing(object sender, FormClosingEventArgs e)
        {
            GlobalHotkeys.UnregisterAll();
            Program.settings.Save();
        }

        private void FormSwitcher_Deactivate(object sender, EventArgs e)
        {
            Hide();
            timer1.Enabled = false;
            RenderType = Program.settings.DefaultDataFlow;
            RefreshDevices(RenderType);
            VolBar.RegisterDevice(RenderType);
        }

        private void pictureBox1_Paint(object sender, PaintEventArgs e)
        {
            using (var pen = new Pen(SystemColors.ScrollBar))
                e.Graphics.DrawLine(pen, 0, 0, pictureItemsBack.Width, 0);
        }

        private void IconChanged(object sender, EventArgs eventArgs)
        {
            if (VolBar.Mute)
                notifyIcon.Icon = ActiveTrayIcons[0];
            else if (VolBar.Value == 0)
                notifyIcon.Icon = ActiveTrayIcons[1];
            else if (VolBar.Value > 0 && VolBar.Value < 0.33)
                notifyIcon.Icon = ActiveTrayIcons[2];
            else if (VolBar.Value > 0.33 && VolBar.Value < 0.66)
                notifyIcon.Icon = ActiveTrayIcons[3];
            else if (VolBar.Value > 0.66 && VolBar.Value <= 1)
                notifyIcon.Icon = ActiveTrayIcons[4];
        }

        private void HotKeyPressed(short id)
        {
            var hkid = Program.settings.Hotkey.First(hkey => hkey.ID == id);

            switch (hkid.Function)
            {
                case HotkeyFunction.SwitchPlaybackDevice:
                    QuickSwitchDevice(EDataFlow.eRender, hkid.ShowOSD);
                    break;

                case HotkeyFunction.SwitchRecordingDevice:
                    QuickSwitchDevice(EDataFlow.eCapture, hkid.ShowOSD);
                    break;

                case HotkeyFunction.TogglePlaybackMute:
                    ChangeDeviceState(EDataFlow.eRender, true, 0, hkid.ShowOSD);
                    break;

                case HotkeyFunction.ToggleRecordingMute:
                    ChangeDeviceState(EDataFlow.eCapture, true, 0, hkid.ShowOSD);
                    break;

                case HotkeyFunction.PlaybackVolumeUp:
                    ChangeDeviceState(EDataFlow.eRender, false, 1, hkid.ShowOSD);
                    break;

                case HotkeyFunction.PlaybackVolumeDown:
                    ChangeDeviceState(EDataFlow.eRender, false, -1, hkid.ShowOSD);
                    break;

                case HotkeyFunction.RecordingVolumeUp:
                    ChangeDeviceState(EDataFlow.eCapture, false, 1, hkid.ShowOSD);
                    break;

                case HotkeyFunction.RecordingVolumeDown:
                    ChangeDeviceState(EDataFlow.eCapture, false, -1, hkid.ShowOSD);
                    break;
            }
        }

        private void QuickSwitchDevice(EDataFlow rType, bool showOSD)
        {
            var devName = EndPoints.SetNextDefault(rType);

            if (showOSD)
                Program.frmOSD.ChangeDevice(devName);
        }

        private void ChangeDeviceState(EDataFlow rType, bool toggleMute, int volChange, bool showOSD)
        {
            var MMDevice = EndPoints.GetDefaultMMDevice(rType);
            if (toggleMute)
            {
                var mute = MMDevice.AudioEndpointVolume.Mute = !MMDevice.AudioEndpointVolume.Mute;
                if (showOSD)
                    Program.frmOSD.ChangeMute(mute, MMDevice.AudioEndpointVolume.MasterVolumeLevelScalar);
            }
            else if (volChange != 0)
            {
                var vol = CalcVol(MMDevice.AudioEndpointVolume.MasterVolumeLevelScalar, volChange);
                MMDevice.AudioEndpointVolume.MasterVolumeLevelScalar = vol;

                if (showOSD)
                    Program.frmOSD.ChangeVolume(vol);
            }
        }

        private float CalcVol(float vol, int direction)
        {
            if (direction < 0)
                return vol >= 0.02f ? vol - 0.02f : 0;
            if (direction > 0)
                return vol <= 0.98f ? vol + 0.02f : 1;
            return 0;
        }

        private bool ScrollTheVolume(int delta)
        {
            VolBar.ChangeVolume(CalcVol(VolBar.Value, delta));
            if (Program.settings.VolumeScroll.ShowOSD)
                Program.frmOSD.ChangeVolume(VolBar.Value);
            return false;
        }
        
        private void notifyIcon1_MouseMove(object sender, MouseEventArgs e)
        {
            if (listDevices.SelectedItems.Count > 0)
            {
                var text = listDevices.SelectedItems[0].Text;
                notifyIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
            }
            else
            {
                notifyIcon.Text = "No audio devices found";
            }
        }

        private void notifyIcon1_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
                return;

            var rType = Program.settings.DefaultDataFlow;
            if (ModifierKeys.HasFlag(Keys.Control))
                rType = rType == EDataFlow.eRender ? EDataFlow.eCapture : EDataFlow.eRender;
            
            if (ModifierKeys.HasFlag(Keys.Alt))
            {
                ChangeDeviceState(rType, true, 0, true);
            }
            else
            {
                RenderType = rType;
                RefreshDevices(RenderType);
                SetSizes();
            }
        }

        private void notifyIcon1_MouseUp(object sender, MouseEventArgs e)
        {
            if (ModifierKeys.HasFlag(Keys.Alt))
                return;

            if (listDevices.Items.Count > 0)
                VolBar.RegisterDevice(RenderType);

            if (e.Button == MouseButtons.Left)
            {
                timer1.Enabled = true;
                Show();
                Activate();
            }
        }

        private void SetSizes()
        {
            Height = listDevices.Items.Count * listDevices.TileSize.Height + pictureItemsBack.ClientSize.Height + SystemInformation.FrameBorderSize.Width * 2;
            pictureItemsBack.Top = ClientSize.Height - pictureItemsBack.ClientSize.Height;
            VolBar.Top = pictureItemsBack.Top + pictureItemsBack.Height/2 - VolBar.Height/2;
            ledLeft.Top = VolBar.Top - ledLeft.Height - 1;
            ledRight.Top = VolBar.Top + VolBar.Height + 1;
            var point = WindowPosition.GetWindowPosition(notifyIcon, Width, Height);
            Left = point.X;
            Top = point.Y;
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            var peaks = VolBar.Device.AudioMeterInformation.Channels.GetPeaks();
            ledLeft.SetValue(peaks[0]);
            ledRight.SetValue(peaks[VolBar.Stereo ? 1 : 0]);

            if (!listDevices.Focused)
                listDevices.Focus();
        }

        private void RefreshDevices(EDataFlow renderType)
        {
            listDevices.Clear();
            DeviceIcons.Clear();

            listDevices.BeginUpdate();

            var pDevices = EndPoints.DeviceEnumerator.EnumerateAudioEndPoints(renderType, EDeviceState.Active);
            if (pDevices.Count > 0)
            {
                var defaultDev = EndPoints.DeviceEnumerator.GetDefaultAudioEndpoint(renderType, ERole.eMultimedia).ID;
                var devCount = pDevices.Count;

                for (var i = 0; i < devCount; i++)
                {
                    var device = pDevices[i];
                    var devID = device.ID;

                    DeviceIcons.Add(device.IconPath);
                    var devSettings = Program.settings.Device.Find(x => x.DeviceID == devID);
                    if (devSettings == null || !devSettings.HideFromList)
                    {
                        var item = new ListViewItem
                        {
                            ImageIndex = i,
                            Text = device.FriendlyName,
                            Selected = devID == defaultDev,
                            Tag = devID,
                        };
                        
                        listDevices.Items.Add(item);

                        if (devID == defaultDev)
                        {
                            SetDeviceIcon(i, true);
                            SetTrayIcons();
                        }
                    }
                }
            }

            listDevices.EndUpdate();
        }

        private void SetDeviceIcon(int index, bool isSelected)
        {
            if (listDevices.Items.Count == 0) return;

            listDevices.LargeImageList.Images[index].Dispose();
            listDevices.LargeImageList.Images[index] = isSelected
                                                                   ? DeviceIcons.DefaultIcons.Images[index]
                                                                   : DeviceIcons.NormalIcons.Images[index];
        }

        private void SetTrayIcons()
        {
            var devSettings = Program.settings.Device.Find(x =>
            {
                var dev = listDevices.SelectedItems[0];
                return x.DeviceID == dev.Tag.ToString();
            });
            if (devSettings == null ||
                (devSettings.Hue == 0 &&
                 devSettings.Saturation == 0 &&
                 devSettings.Brightness == 0))
            {
                ActiveTrayIcons = DefaultTrayIcons;
                return;
            }

            var newIcons = DefaultTrayIcons.Select(icon => DeviceIcons.ChangeColors(icon.ToBitmap(), devSettings.Hue, devSettings.Saturation/100f, devSettings.Brightness/100f)).Select(bmp => Icon.FromHandle(bmp.GetHicon())).ToList();

            ActiveTrayIcons = newIcons;
            IconChanged(this, EventArgs.Empty);
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var cfg = new FormSettings())
            {
                RenderType = Program.settings.DefaultDataFlow;
                RefreshDevices(RenderType);
                cfg.ShowDialog();
                ledLeft.OldStyle = Program.settings.ColorVU;
                ledRight.OldStyle = Program.settings.ColorVU;
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
            Application.Exit();
        }

        private void listDevices_Click(object sender, EventArgs e)
        {
            foreach (ListViewItem item in listDevices.Items)
            {
                SetDeviceIcon(item.Index, item.Selected);

                if (item.Selected)
                {
                    listDevices.BeginUpdate();
                    SetTrayIcons();
                    EndPoints.SetDefaultDevice(listDevices.SelectedItems[0].Tag.ToString());
                    VolBar.RegisterDevice(RenderType);
                    listDevices.EndUpdate();
                }
            }
        }
    }
}