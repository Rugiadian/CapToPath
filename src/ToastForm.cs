using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapToPath
{
    public sealed class ToastForm : Form
    {
        // P/Invoke for rounded corners and styling
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWCP_ROUND = 2;

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;

        private const int WS_EX_TOPMOST = 0x00000008;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        private readonly string _title;
        private readonly string _filePath;
        private readonly bool _isSuccess;
        private readonly System.Windows.Forms.Timer _animTimer;

        private enum State
        {
            FadeIn,
            Display,
            FadeOut,
            Completed
        }

        private State _state = State.FadeIn;
        private DateTime _displayStartTime;

        public ToastForm(string title, string filePath, bool isSuccess = true)
        {
            _title = title ?? "스크린샷 경로가 복사되었습니다.";
            _filePath = filePath ?? string.Empty;
            _isSuccess = isSuccess;

            // Form properties
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            Opacity = 0.0;
            Size = new Size(390, 78);
            BackColor = Color.FromArgb(32, 32, 32);

            // Compute right-bottom position (above taskbar)
            Rectangle workArea = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
            int margin = 16;
            Location = new Point(workArea.Right - Width - margin, workArea.Bottom - Height - margin);

            // Click anywhere to dismiss immediately
            Click += (s, e) => CloseToast();
            Cursor = Cursors.Hand;

            // Animation Timer (fires ~60fps)
            _animTimer = new System.Windows.Forms.Timer
            {
                Interval = 15
            };
            _animTimer.Tick += OnAnimTick;
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);

            // Try applying Windows 11 round corners if supported
            try
            {
                int preference = DWMWCP_ROUND;
                DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref preference, sizeof(int));
            }
            catch
            {
                // Ignore on older Windows versions
            }

            // Show window without taking focus
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            _animTimer.Start();
        }

        private void OnAnimTick(object? sender, EventArgs e)
        {
            switch (_state)
            {
                case State.FadeIn:
                    Opacity = Math.Min(1.0, Opacity + 0.12);
                    if (Opacity >= 1.0)
                    {
                        Opacity = 1.0;
                        _state = State.Display;
                        _displayStartTime = DateTime.UtcNow;
                    }
                    break;

                case State.Display:
                    // If error, stay visible slightly longer (2.0s) so the user can read what happened
                    double stayDuration = _isSuccess ? 1200 : 2500;
                    if ((DateTime.UtcNow - _displayStartTime).TotalMilliseconds >= stayDuration)
                    {
                        _state = State.FadeOut;
                    }
                    break;

                case State.FadeOut:
                    Opacity = Math.Max(0.0, Opacity - 0.10);
                    if (Opacity <= 0.0)
                    {
                        CloseToast();
                    }
                    break;
            }
        }

        private void CloseToast()
        {
            _animTimer.Stop();
            _animTimer.Dispose();
            _state = State.Completed;
            Close();
            Application.Exit();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);

            // Rounded border & background fill
            int cornerRadius = 12;
            using (GraphicsPath path = CreateRoundedRectanglePath(rect, cornerRadius))
            {
                // Background
                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(32, 32, 32)))
                {
                    g.FillPath(bgBrush, path);
                }

                // Border: subtle grey or reddish tint on error
                Color borderColor = _isSuccess ? Color.FromArgb(60, 60, 60) : Color.FromArgb(160, 50, 50);
                using (Pen borderPen = new Pen(borderColor, 1f))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            // Left icon badge
            int iconSize = 36;
            int iconX = 18;
            int iconY = (Height - iconSize) / 2;
            Rectangle iconRect = new Rectangle(iconX, iconY, iconSize, iconSize);

            Color badgeColor = _isSuccess ? Color.FromArgb(16, 124, 65) : Color.FromArgb(196, 43, 28);
            using (SolidBrush iconBg = new SolidBrush(badgeColor))
            {
                g.FillEllipse(iconBg, iconRect);
            }

            if (_isSuccess)
            {
                // Draw checkmark
                using (Pen checkPen = new Pen(Color.White, 2.5f))
                {
                    checkPen.StartCap = LineCap.Round;
                    checkPen.EndCap = LineCap.Round;

                    PointF p1 = new PointF(iconX + 10f, iconY + 18.5f);
                    PointF p2 = new PointF(iconX + 15.5f, iconY + 24.5f);
                    PointF p3 = new PointF(iconX + 26f, iconY + 13f);

                    g.DrawLine(checkPen, p1, p2);
                    g.DrawLine(checkPen, p2, p3);
                }
            }
            else
            {
                // Draw X (error icon)
                using (Pen crossPen = new Pen(Color.White, 2.5f))
                {
                    crossPen.StartCap = LineCap.Round;
                    crossPen.EndCap = LineCap.Round;

                    g.DrawLine(crossPen, iconX + 12f, iconY + 12f, iconX + 24f, iconY + 24f);
                    g.DrawLine(crossPen, iconX + 24f, iconY + 12f, iconX + 12f, iconY + 24f);
                }
            }

            // Title & Path Text layout
            int textX = iconX + iconSize + 14;
            int textWidth = Width - textX - 16;

            // Title
            using (Font titleFont = new Font("Malgun Gothic", 9.75f, FontStyle.Bold))
            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(245, 245, 245)))
            {
                Rectangle titleRect = new Rectangle(textX, 16, textWidth, 20);
                TextRenderer.DrawText(
                    g,
                    _title,
                    titleFont,
                    titleRect,
                    titleBrush.Color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding
                );
            }

            // Path Subtext
            string displayPath = string.IsNullOrWhiteSpace(_filePath)
                ? "(경로 없음)"
                : _filePath;

            using (Font pathFont = new Font("Malgun Gothic", 8.25f, FontStyle.Regular))
            using (SolidBrush pathBrush = new SolidBrush(Color.FromArgb(180, 180, 180)))
            {
                Rectangle pathRect = new Rectangle(textX, 39, textWidth, 18);
                TextRenderer.DrawText(
                    g,
                    displayPath,
                    pathFont,
                    pathRect,
                    pathBrush.Color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPadding
                );
            }
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animTimer?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
