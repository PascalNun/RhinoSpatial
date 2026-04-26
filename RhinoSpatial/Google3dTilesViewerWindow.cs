using System;
using Eto.Drawing;
using Eto.Forms;
using Rhino;
using Rhino.UI;

namespace RhinoSpatial
{
    internal sealed class Google3dTilesViewerWindow : Form
    {
        private static Google3dTilesViewerWindow? _instance;
        private readonly WebView _webView;
        private bool _isBackgroundMode;

        private Google3dTilesViewerWindow()
        {
            Title = "RhinoSpatial 3D Tiles Viewer (Google)";
            ClientSize = new Size(1320, 860);
            Padding = new Padding(0);
            Resizable = true;
            ShowInTaskbar = false;

            this.UseRhinoStyle();

            _webView = new WebView();
            Content = _webView;

            Closed += (_, _) =>
            {
                _instance = null;
            };
        }

        public static void ShowOrUpdate(string url, bool visible)
        {
            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                _instance ??= new Google3dTilesViewerWindow();
                _instance.LoadUrl(url, visible);

                if (visible)
                {
                    _instance.PrepareDiagnosticMode();
                }
                else
                {
                    _instance.PrepareBackgroundMode();
                }

                var activeDocument = RhinoDoc.ActiveDoc;
                if (activeDocument is not null)
                {
                    _instance.Show(activeDocument);
                }
                else
                {
                    _instance.Show();
                }

                _instance.ApplyRequestedMode();
            }));
        }

        private void LoadUrl(string url, bool visible)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                if (_webView.Url is null || _webView.Url != uri)
                {
                    _webView.Url = uri;
                }
            }

            _isBackgroundMode = !visible;
        }

        private void PrepareBackgroundMode()
        {
            if (!_isBackgroundMode)
            {
                return;
            }

            ClientSize = new Size(8, 8);
            Location = new Point(-20000, -20000);
        }

        private void PrepareDiagnosticMode()
        {
            _isBackgroundMode = false;
            ClientSize = new Size(1320, 860);
            Location = new Point(120, 120);
        }

        private void ApplyRequestedMode()
        {
            if (_isBackgroundMode)
            {
                Location = new Point(-20000, -20000);
                return;
            }

            BringToFront();
        }
    }
}
