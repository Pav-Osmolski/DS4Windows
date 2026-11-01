using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4Windows;

internal static class SpecialActionImport
{
    internal static List<SpecialAction> Read(Stream source, IEnumerable<string> existingNames)
    {
        using var reader = XmlReader.Create(source, new XmlReaderSettings {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
        var dto = (ActionsDTO)new XmlSerializer(typeof(ActionsDTO)).Deserialize(reader);
        if (dto.actionSerializers.Count == 0 || dto.actionSerializers.Count > 512)
            throw new InvalidDataException("Choose an Actions.xml file containing 1 to 512 actions.");
        foreach (var action in dto.actionSerializers)
        {
            if (action == null || string.IsNullOrWhiteSpace(action.Name) || action.Name.Length > 128 ||
                string.IsNullOrWhiteSpace(action.Trigger) || action.Trigger.Length > 1024 || action.Details == null || action.Details.Length > 65536 ||
                !double.IsFinite(action.Delay) || action.Delay < 0 || action.Delay > 60)
                throw new InvalidDataException("An imported action has invalid or oversized fields.");
        }
        var temporary = new BackingStore();
        temporary.actions.Clear();
        dto.MapTo(temporary);
        if (temporary.actions.Count != dto.actionSerializers.Count || temporary.actions.Any(a =>
            a.typeID == SpecialAction.ActionTypeId.None || a.trigger.Count == 0 || a.trigger.Contains(DS4Controls.None)))
            throw new InvalidDataException("The file contains an unsupported action or trigger.");
        if (temporary.actions.Any(a => a.typeID == SpecialAction.ActionTypeId.Key &&
            !ushort.TryParse(a.details, out _)))
            throw new InvalidDataException("An imported key action has an invalid key code.");
        var names = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        foreach (var action in temporary.actions)
        {
            string original = action.name;
            int suffix = 1;
            while (!names.Add(action.name)) action.name = original + " (imported " + suffix++ + ")";
        }
        return temporary.actions;
    }

    internal static void SaveMerged(string path, IEnumerable<SpecialAction> existing, IEnumerable<SpecialAction> imported)
    {
        var store = new BackingStore();
        store.actions.Clear(); store.actions.AddRange(existing); store.actions.AddRange(imported);
        var dto = new ActionsDTO(); dto.MapFrom(store);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var writer = XmlWriter.Create(stream, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = true, CloseOutput = false }))
                    new XmlSerializer(typeof(ActionsDTO)).Serialize(writer, dto);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".before-import-" + Guid.NewGuid().ToString("N") + ".bak");
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
