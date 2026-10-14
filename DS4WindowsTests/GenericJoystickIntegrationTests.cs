using System;
using System.Xml;
using System.Xml.Serialization;
using System.IO;
using DS4Windows;
using DS4WinWPF.DS4Control.DTOXml;

namespace DS4WindowsTests;

[TestClass]
public class GenericJoystickIntegrationTests
{
    [TestMethod]
    public void BackendContractRejectsWrongRestAndMalformedReadiness()
    {
        const string response = "{\"contract\":\"genericjoystick-v1\",\"version\":1,\"experimental\":true,\"buttonCount\":12,\"neutralAxes\":[0,0,0,-32767,-32767,0],\"inputConfigured\":true}";
        using var valid = System.Text.Json.JsonDocument.Parse(response);
        Assert.IsTrue(GenericJoystickContract.ReadConfigured(valid.RootElement, new short[] { 0, 0, 0, -32767, -32767, 0 }));
        Assert.ThrowsExactly<IOException>(() => GenericJoystickContract.ReadConfigured(valid.RootElement, new short[6]));
        foreach (string malformed in new[] { response.Replace("\"version\":1", "\"version\":\"1\""),
            response.Replace("\"inputConfigured\":true", "\"inputConfigured\":\"true\""),
            response.Replace("\"buttonCount\":12", "\"buttonCount\":16"), response.Replace("-32767", "-32768") })
        {
            using var doc = System.Text.Json.JsonDocument.Parse(malformed);
            Assert.ThrowsExactly<IOException>(() => GenericJoystickContract.ReadConfigured(doc.RootElement));
        }
        using var pending = System.Text.Json.JsonDocument.Parse(response.Replace("\"inputConfigured\":true", "\"inputConfigured\":false"));
        Assert.IsFalse(GenericJoystickContract.ReadConfigured(pending.RootElement));
    }

    [TestMethod]
    public void PreparedProfileSerializerPreservesOutputIdentityAndCustomTriggerRest()
    {
        var source = new BackingStore();
        source.outputDevType[0] = OutContType.ViiperGenericJoystick;
        source.genericJoystickSettings[0].Options = GenericJoystickOptions.Default with {
            Layout = GenericJoystickLayout.Racing, TriggerMode = GenericJoystickTriggerMode.FullRange, InvertLeftTrigger = true };
        var profile = new ProfileDTO { DeviceIndex = 0 };
        profile.MapFrom(source);
        var serializer = ProfileDTO.Serializer;
        using var writer = new StringWriter();
        serializer.Serialize(writer, profile);
        Assert.Contains("ViiperGenericJoystick", writer.ToString());
        using var reader = new StringReader(writer.ToString());
        var loaded = (ProfileDTO)serializer.Deserialize(reader);
        loaded.DeviceIndex = 0;
        var destination = new BackingStore();
        loaded.MapTo(destination);
        Assert.AreEqual(OutContType.ViiperGenericJoystick, destination.outputDevType[0]);
        Assert.AreEqual(source.genericJoystickSettings[0].Options, destination.genericJoystickSettings[0].Options);
    }

    [TestMethod]
    public void OldProfilesUseExplicitDefaultsAndMalformedProfilesAreNotActivated()
    {
        Assert.AreEqual(GenericJoystickOptions.Default, GenericJoystickProfileSettings.Read(null).Options);
        var document = new XmlDocument();
        document.LoadXml("<GenericJoystick Version='999'/>");
        var invalid = GenericJoystickProfileSettings.Read(document.DocumentElement);
        Assert.IsFalse(invalid.Valid);
        Assert.ThrowsExactly<InvalidOperationException>(() => invalid.Write(document));
    }

    [TestMethod]
    public void EveryLayoutAndTriggerModeRoundTripsWithoutChangingNeutral()
    {
        foreach (var layout in Enum.GetValues<GenericJoystickLayout>())
        foreach (var mode in Enum.GetValues<GenericJoystickTriggerMode>())
        {
            var settings = new GenericJoystickProfileSettings { Options = GenericJoystickOptions.Default with
                { Layout = layout, TriggerMode = mode, InvertCombinedAxis = true, ButtonThreshold = 200 } };
            var document = new XmlDocument();
            var loaded = GenericJoystickProfileSettings.Read(settings.Write(document));
            Assert.IsTrue(loaded.Valid);
            Assert.AreEqual(settings.Options, loaded.Options);
            Assert.AreEqual(GenericJoystickMapping.Neutral(settings.Options), GenericJoystickMapping.Neutral(loaded.Options));
        }
    }

    [TestMethod]
    public void MappedWriterProjectionMatchesTheValidatedMappingPolicy()
    {
        var random = new Random(614);
        foreach (var layout in Enum.GetValues<GenericJoystickLayout>())
        foreach (var mode in Enum.GetValues<GenericJoystickTriggerMode>())
        for (int i = 0; i < 100; i++)
        {
            var state = new DS4State {
                LX = (byte)random.Next(256), LY = (byte)random.Next(256), RX = (byte)random.Next(256), RY = (byte)random.Next(256),
                L2 = (byte)random.Next(256), R2 = (byte)random.Next(256),
                Cross = random.Next(2) == 1, Circle = random.Next(2) == 1, Square = random.Next(2) == 1, Triangle = random.Next(2) == 1,
                L1 = random.Next(2) == 1, R1 = random.Next(2) == 1, Share = random.Next(2) == 1, Options = random.Next(2) == 1,
                L3 = random.Next(2) == 1, R3 = random.Next(2) == 1,
                DpadUp = random.Next(2) == 1, DpadRight = random.Next(2) == 1, DpadDown = random.Next(2) == 1, DpadLeft = random.Next(2) == 1
            };
            var options = GenericJoystickOptions.Default with { Layout = layout, TriggerMode = mode };
            var mapped = ViiperStatePacketBuilder.BuildMappedState(state, -1, followDigitalTriggers: false);
            Assert.AreEqual(GenericJoystickMapping.Build(state, options), GenericJoystickMappedBridge.Build(mapped, options));
        }
    }

    [TestMethod]
    public void JoystickOutputAppendsExistingIdentitiesAndHasNoFeedback()
    {
        Assert.AreEqual(8u, (uint)OutContType.ViiperXboxOne);
        Assert.AreEqual(9u, (uint)OutContType.ViiperGenericJoystick);
        Assert.AreEqual(OutContType.ViiperGenericJoystick, OutContType.ViiperGenericJoystick.Normalize());
        Assert.AreEqual("genericjoystick", ViiperStatePacketBuilder.GetViiperDeviceName(ViiperVirtualDeviceType.GenericJoystick));
        Assert.AreEqual(0, ViiperStatePacketBuilder.GetFeedbackLength(ViiperVirtualDeviceType.GenericJoystick));
        Assert.AreEqual(16, ViiperStatePacketBuilder.GetPacketSize(ViiperVirtualDeviceType.GenericJoystick));
    }

    [TestMethod]
    public void ReleasedDigitalFollowersDoNotChangeJoystickAnalogAxes()
    {
        var state = new DS4State { LX = 128, LY = 128, RX = 128, RY = 128, L2Btn = true, R2Btn = true };
        var mapped = ViiperStatePacketBuilder.BuildMappedState(state, -1, followDigitalTriggers: false);
        Assert.AreEqual(GenericJoystickMapping.Neutral(GenericJoystickOptions.Default), GenericJoystickMappedBridge.Build(mapped, GenericJoystickOptions.Default));
    }
}

