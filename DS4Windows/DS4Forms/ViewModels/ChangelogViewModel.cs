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
using System.ComponentModel;
using System.Threading.Tasks;
using DS4Windows;

namespace DS4WinWPF.DS4Forms.ViewModels;

public class ChangelogViewModel : INotifyPropertyChanged
{
    private readonly Func<Task<string>> load;
    public ChangelogViewModel() : this(() => Changelog.GetChangelogMarkdown(true)) { }
    internal ChangelogViewModel(Func<Task<string>> load) => this.load = load;

    public string Markdown { get; private set; } = string.Empty;
    public bool IsLoading { get; private set; }
    public bool HasError { get; private set; }
    public bool ShowStatus => IsLoading || HasError;
    public string StatusText { get; private set; } = global::DS4Windows.UiText.Get("Loading release notes...");
    public event PropertyChangedEventHandler PropertyChanged;

    public async Task DisplayChangelog()
    {
        if (IsLoading) return;
        IsLoading = true;
        HasError = false;
        StatusText = global::DS4Windows.UiText.Get("Loading release notes...");
        Notify();
        try
        {
            string notes = await load();
            if (string.IsNullOrWhiteSpace(notes))
                throw new InvalidOperationException(global::DS4Windows.UiText.Get("No release notes were returned."));
            Markdown = notes;
            StatusText = string.Empty;
        }
        catch (Exception error)
        {
            HasError = true;
            StatusText = global::DS4Windows.UiText.Get("Release notes could not be loaded. Check your connection and try again, or view releases on GitHub.");
            NLog.LogManager.GetCurrentClassLogger().Warn(error, "Could not load the changelog.");
        }
        finally
        {
            IsLoading = false;
            Notify();
        }
    }

    private void Notify()
    {
        foreach (string name in new[] { nameof(Markdown), nameof(IsLoading), nameof(HasError), nameof(ShowStatus), nameof(StatusText) })
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
