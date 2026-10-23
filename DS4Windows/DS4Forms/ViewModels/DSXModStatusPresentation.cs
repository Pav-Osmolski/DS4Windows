namespace DS4WinWPF.DS4Forms.ViewModels
{
    internal static class DSXModStatusPresentation
    {
        internal static string Describe(bool requested, bool serviceRunning, bool listening,
            string address, int port, string error, bool changing = false)
        {
            if (changing) return global::DS4Windows.UiText.Get("Applying game mod settings…");
            if (!string.IsNullOrWhiteSpace(error)) return error;
            if (!requested) return global::DS4Windows.UiText.Get("Off — game mods cannot change your controller.");
            if (!serviceRunning) return global::DS4Windows.UiText.Get("Enabled — press Start in DS4Windows to accept game mods.");
            if (!listening) return global::DS4Windows.UiText.Get("Not listening — open Connection details and choose Apply / Retry.");
            string endpoint = address?.Contains(':') == true ? $"[{address}]:{port}" : $"{address}:{port}";
            return global::DS4Windows.UiText.Format("Listening on {0}. Connect a DualSense or DualSense Edge for mod effects.", endpoint);
        }
    }
}
