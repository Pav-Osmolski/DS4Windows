/*
DS4Windows
Copyright (C) 2023  Travis Nickles

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <https://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DS4WinWPF
{
    public class ProfileEntity : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
        public string Summary { get; private set; }
        public DateTime ModifiedUtc { get; private set; }
        public string ModifiedDescription => ModifiedUtc == default ? string.Empty :
            DS4Windows.UiText.Format("Modified {0:g}", ModifiedUtc.ToLocalTime());

        internal void RefreshMetadata()
        {
            Summary = DS4Windows.UiText.Get("Controller profile");
            ModifiedUtc = default;
            try
            {
                string path = Path.Combine(DS4Windows.Global.appdatapath, "Profiles", name + ".xml");
                if (File.Exists(path))
                {
                    ModifiedUtc = File.GetLastWriteTimeUtc(path);
                    var document = new System.Xml.XmlDocument { XmlResolver = null };
                    using var reader = System.Xml.XmlReader.Create(path, new System.Xml.XmlReaderSettings
                        { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16 * 1024 * 1024 });
                    document.Load(reader);
                    Summary = document.DocumentElement?.SelectSingleNode("OutputContDevice")?.InnerText ?? Summary;
                    if (App.rootHub != null && Enumerable.Range(0, Math.Min(DS4Windows.Global.ProfilePath.Length, App.rootHub.DS4Controllers.Length))
                        .Any(index => App.rootHub.DS4Controllers[index] != null && DS4Windows.Global.ProfilePath[index] == name))
                        Summary += " · " + DS4Windows.UiText.Get("In use");
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Xml.XmlException or ArgumentException) { }
            PropertyChanged?.Invoke(this, new(nameof(Summary)));
            PropertyChanged?.Invoke(this, new(nameof(ModifiedUtc)));
            PropertyChanged?.Invoke(this, new(nameof(ModifiedDescription)));
        }
        private string name;
        public string Name
        {
            get => name;
            set
            {
                if (name == value) return;
                name = value;
                RefreshMetadata();
                PropertyChanged?.Invoke(this, new(nameof(Name)));
                NameChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        public event EventHandler NameChanged;
        public event EventHandler ProfileSaved;
        public event EventHandler ProfileDeleted;

        public void DeleteFile()
        {
            if (!DS4Windows.SettingsStorage.CanWrite(Path.Combine(DS4Windows.Global.appdatapath, "Profiles"), out string reason))
                throw new IOException(reason);
            if (!string.IsNullOrWhiteSpace(name))
            {
                string filepath = DS4Windows.Global.appdatapath + @"\Profiles\" + name + ".xml";
                if (File.Exists(filepath))
                {
                    File.Delete(filepath);
                    ProfileDeleted?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public bool SaveProfile(int deviceNum, string newName = null)
        {
            string targetName;
            try { targetName = DS4Windows.ProfileName.Normalize(newName ?? name,
                existingIdentity: newName == null || newName == name); }
            catch (ArgumentException) { return false; }
            if (string.IsNullOrWhiteSpace(targetName) ||
                !DS4Windows.Global.SaveProfile(deviceNum, targetName, name))
                return false;

            Name = targetName;
            DS4Windows.Global.CacheExtraProfileInfo(deviceNum);
            return true;
        }

        public void FireSaved()
        {
            RefreshMetadata();
            ProfileSaved?.Invoke(this, EventArgs.Empty);
        }

        public bool RenameProfile(string newProfileName, out string error)
        {
            error = null;
            if (!DS4Windows.SettingsStorage.CanWrite(Path.Combine(DS4Windows.Global.appdatapath, "Profiles"), out error)) return false;
            string target;
            try { target = DS4Windows.ProfileName.Normalize(newProfileName,
                existingIdentity: string.Equals(newProfileName, name, StringComparison.OrdinalIgnoreCase)); }
            catch (ArgumentException)
            {
                error = Properties.Resources.ResourceManager.GetString("ProfileNameInvalid");
                return false;
            }
            if (target == name) return true;
            string oldFilePath = Path.Combine(DS4Windows.Global.appdatapath, "Profiles", name + ".xml");
            string newFilePath = Path.Combine(DS4Windows.Global.appdatapath, "Profiles", target + ".xml");
            try
            {
                if (!string.Equals(target, name, StringComparison.OrdinalIgnoreCase) && File.Exists(newFilePath))
                {
                    error = Properties.Resources.ResourceManager.GetString("ProfileRenameCollision");
                    return false;
                }
                // Non-overwriting move also protects a collision appearing
                // after the preflight check. Keep the serialized file intact.
                File.Move(oldFilePath, newFilePath);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = Properties.Resources.ResourceManager.GetString("ProfileRenameFailed") + " " + ex.Message;
                DS4Windows.AppLogger.LogToGui(error, false);
                return false;
            }
            Name = target;
            return true;
        }
    }
}
