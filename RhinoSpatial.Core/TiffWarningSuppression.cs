using System;
using System.Globalization;
using System.Threading;
using BitMiracle.LibTiff.Classic;

namespace RhinoSpatial.Core
{
    internal static class TiffWarningSuppression
    {
        private static int _installed;

        public static void EnsureInstalled()
        {
            if (Interlocked.Exchange(ref _installed, 1) == 1)
            {
                return;
            }

            Tiff.SetErrorHandler(new RhinoSpatialTiffErrorHandler());
        }

        private sealed class RhinoSpatialTiffErrorHandler : TiffErrorHandler
        {
            public override void WarningHandler(Tiff tif, string method, string format, params object[] args)
            {
                if (ShouldSuppressWarning(format, args))
                {
                    return;
                }

                base.WarningHandler(tif, method, format, args);
            }

            public override void WarningHandlerExt(Tiff tif, object clientData, string method, string format, params object[] args)
            {
                if (ShouldSuppressWarning(format, args))
                {
                    return;
                }

                base.WarningHandlerExt(tif, clientData, method, format, args);
            }

            private static bool ShouldSuppressWarning(string format, object[] args)
            {
                var message = TryFormatMessage(format, args);
                if (string.IsNullOrWhiteSpace(message))
                {
                    return false;
                }

                return message.Contains("invalid TIFF directory; tags are not sorted in ascending order", StringComparison.OrdinalIgnoreCase) ||
                       message.Contains("unknown field with tag", StringComparison.OrdinalIgnoreCase);
            }

            private static string TryFormatMessage(string format, object[] args)
            {
                try
                {
                    return args is { Length: > 0 }
                        ? string.Format(CultureInfo.InvariantCulture, format, args)
                        : format;
                }
                catch
                {
                    return format;
                }
            }
        }
    }
}
