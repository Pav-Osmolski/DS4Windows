using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using DS4Windows;
using DS4Windows.InputDevices;
using DS4WinWPF.DS4Forms;
using DS4WinWPF.DS4Forms.Themes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DS4WindowsTests;

[TestClass]
public sealed class ControllerInfoTests
{
    internal static byte[] DualSenseReport()
    {
        var b = new byte[64]; b[0] = 0x20;
        Encoding.ASCII.GetBytes("Jul 04 2025").CopyTo(b,1);
        Encoding.ASCII.GetBytes("10:10:32").CopyTo(b,12);
        b[20]=3;b[21]=1;b[22]=0x44;
        Write(b,24,0x01000208);Write(b,28,0x01000036);
        b[32]=0xc1;b[33]=0xc8;b[44]=0x30;b[45]=6;
        Write(b,48,20);Write(b,52,0x0001000b);Write(b,56,6);
        return b;
    }
    private static void Write(byte[] b,int offset,uint v) => System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset),v);
    internal static ViiperControllerInfoV1 VirtualSample() => new()
    {
        Version=1,BusId=1,DevId="1",Type="dualsense",VendorId=0x054c,ProductId=0x0ce6,UsbRevision=0x0100,
        Product="DualSense Wireless Controller",Manufacturer="Sony Interactive Entertainment",Serial="0000000000000001",
        FactoryReport=DualSenseReport(),Metadata=JsonSerializer.SerializeToElement(new { battery_status=0x2a }),
    };

    [TestMethod]
    public void SonyFirmwareFieldsMatchKnownHostDecodedValues()
    {
        var rows=ControllerInfo.DecodeFactoryReport(DualSenseReport());
        foreach(string value in new[]{"Jul 04 2025 10:10:32","0x01000208","0x0103","0x0044","6.30","0.0.20","1.0.54","0001_000B","0.0.6"})
            Assert.IsTrue(rows.Any(r=>r.Value==value),"Missing wire-decoded value: "+value);
        var ds4=new byte[49];ds4[0]=0xa3;
        Encoding.ASCII.GetBytes("Sep 17 2021").CopyTo(ds4,1);
        Encoding.ASCII.GetBytes("11:34:00").CopyTo(ds4,17);
        Assert.IsTrue(ControllerInfo.DecodeFactoryReport(ds4).Any(r=>r.Value=="Sep 17 2021 11:34:00"));
        // A host decoder must not copy the old emulation bug into its own fields.
        Assert.IsFalse(ControllerInfo.DecodeFactoryReport(ds4).Any(r=>r.Value.Contains("20211")));
    }

    [TestMethod]
    public void MalformedOrUnsupportedReportsDoNotManufactureFactoryValues()
    {
        foreach(byte[] report in new[]{Array.Empty<byte>(),new byte[]{0xff},new byte[49],new byte[64],DualSenseReport()[..63]})
            Assert.AreEqual(0,ControllerInfo.DecodeFactoryReport(report).Count);
        var invalid=DualSenseReport();invalid[1]=0x01;
        Assert.AreEqual(0,ControllerInfo.DecodeFactoryReport(invalid).Count);
        Assert.ThrowsExactly<System.IO.InvalidDataException>(()=>ControllerInfo.Emulated(new(){Version=2}));
    }

    [TestMethod]
    public void UnsupportedFieldsRemainAbsentAndXboxPublicVersionsArePreserved()
    {
        var info=new ViiperControllerInfoV1 { Version=1,Type="xboxone",Product="Xbox One Controller",VendorId=0x045e,ProductId=0x02d1,
            Details=new(){["firmwareVersion"]="1.2.3.4",["hardwareVersion"]="5.6",["deviceInfo"]="0x0000FFFB01020304"} };
        var pane=ControllerInfo.Emulated(info);
        Assert.IsTrue(pane.Sections.SelectMany(s=>s.Rows).Any(r=>r.Value=="1.2.3.4"));
        Assert.IsFalse(pane.Sections.SelectMany(s=>s.Rows).Any(r=>r.Value.Contains("2025")));
        Assert.IsFalse(pane.Sections.SelectMany(s=>s.Rows).Any(r=>r.Value.Contains("100%")));
        info.Details=null;
        var fallback=ControllerInfo.Emulated(info);
        Assert.IsTrue(fallback.Sections.SelectMany(s=>s.Rows).Any(r=>r.Value.Contains("does not expose")));
    }

    [TestMethod]
    [DataRow(0x15,"50%")]
    [DataRow(0x2a,"100%")]
    public void VirtualStatusDecodesTheActualSnapshot(int battery,string percent)
    {
        var info=VirtualSample();info.Metadata=JsonSerializer.SerializeToElement(new {battery_status=battery});
        Assert.IsTrue(ControllerInfo.Emulated(info).Sections.SelectMany(s=>s.Rows).Any(r=>r.Value==percent));
    }

    [TestMethod]
    public void UnknownBatteryValuesAreNotReportedAsFull()
    {
        var info=VirtualSample();info.Metadata=JsonSerializer.SerializeToElement(new {battery_status="unknown"});
        Assert.IsFalse(ControllerInfo.Emulated(info).Sections.SelectMany(s=>s.Rows).Any(r=>r.Value.EndsWith("%")));
        info.Metadata=JsonSerializer.SerializeToElement(new {battery_status=0xff});
        Assert.IsFalse(ControllerInfo.Emulated(info).Sections.SelectMany(s=>s.Rows).Any(r=>r.Value.EndsWith("%")));
    }

    [TestMethod]
    public void NintendoStatusUsesItsReportedBandAndMeasuredMillivolts()
    {
        var info=new ViiperControllerInfoV1 {Version=1,Type="ns2pro",Product="Pro Controller",
            Metadata=JsonSerializer.SerializeToElement(new {battery_level=4,charging=true,external_power=true,battery_volts=3861})};
        var rows=ControllerInfo.Emulated(info).Sections.SelectMany(s=>s.Rows).ToArray();
        Assert.IsTrue(rows.Any(r=>r.Value=="4 / 9"));
        Assert.IsTrue(rows.Any(r=>r.Value==3.861.ToString("0.###",CultureInfo.CurrentCulture)+" V"));
        Assert.IsFalse(rows.Any(r=>r.Value.EndsWith("%")),"Do not invent a precise percent from a protocol band.");
    }

    [TestMethod]
    public void FirmwareCacheIsImmutableAndCopyIncludesBothDistinctPersonas()
    {
        var device=new CachedDevice();var report=DualSenseReport();device.Store(report);report[1]=0;
        var one=device.GetControllerFactoryReport();Assert.AreEqual((byte)'J',one[1]);one[1]=0;
        Assert.AreEqual((byte)'J',device.GetControllerFactoryReport()[1]);
        var snapshot=new ControllerInfoSnapshot(ControllerInfo.Physical(device),ControllerInfo.Emulated(VirtualSample()));
        string copy=ControllerInfo.CopyText(snapshot);
        Assert.IsTrue(copy.Contains("Physical controller"));Assert.IsTrue(copy.Contains("Emulated controller"));
        Assert.IsTrue(copy.Contains("DualSense Wireless Controller"));
    }
    private sealed class CachedDevice : DS4Device
    {
        internal CachedDevice():base("Physical test controller",InputDeviceType.DualSense,ConnectionType.BT){}
        internal void Store(byte[] report)=>CacheControllerFactoryReport(report);
    }
}

internal static class ControllerInfoWindowValidation
{
    internal static void Validate(Application application,string directory,List<string> files)
    {
        var original=application.Resources.MergedDictionaries.ToArray();
        var shutdown=application.ShutdownMode;
        application.ShutdownMode=ShutdownMode.OnExplicitShutdown;
        directory=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SourcePath()),"..","artifacts","controller-info-renders"));
        System.IO.Directory.CreateDirectory(directory);
        try
        {
            foreach(var choice in new[]{AppThemeChoice.Light,AppThemeChoice.Dark,AppThemeChoice.Graphite,AppThemeChoice.Paper,AppThemeChoice.Midnight,AppThemeChoice.HighContrast})
            {
                string theme=choice.ToString();
                application.Resources.MergedDictionaries.Clear();
                application.Resources.MergedDictionaries.Add(AppThemeCatalog.Create(choice,highContrast:false));
                application.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/DS4Windows;component/DS4Forms/Themes/BridgeShellStyles.xaml",UriKind.Relative)});
                var snapshot=new ControllerInfoSnapshot(
                    new("Information reported by your connected physical controller.",new[]{new ControllerInfoSection("Identity",new[]{new ControllerInfoRow("Controller","Physical DualSense"),new ControllerInfoRow("Connection","USB")})}),
                    ControllerInfo.Emulated(ControllerInfoTests.VirtualSample()));
                var window=new ControllerInfoWindow();window.SetPreviewSnapshot(snapshot);
                var tabs=(TabControl)window.FindName("infoTabs");
                var content=(FrameworkElement)window.Content;
                ((Panel)content).Background=(Brush)application.FindResource("SurfaceBackgroundColor");
                foreach(int tab in new[]{0,1})
                {
                    tabs.SelectedIndex=tab;content.Measure(new Size(820,740));content.Arrange(new Rect(0,0,820,740));content.UpdateLayout();
                    var textboxes=ThemeConsistencyValidation.Visuals(content).OfType<TextBox>().ToArray();
                    Assert.IsTrue(textboxes.Length>0,"Controller info rows must render.");
                    Assert.IsTrue(textboxes.All(t=>t.IsReadOnly && t.ActualWidth>120),theme+" tab "+tab+": "+string.Join(", ",textboxes.Select(t=>$"{t.Text}={t.ActualWidth}")));
                    Assert.IsTrue(textboxes.All(t=>t.ActualHeight<=24),"Single-line info values should remain compact.");
                    Assert.AreEqual(new Thickness(0),tabs.BorderThickness,"No bottom tab-control rule is needed.");
                    foreach(TabItem tabItem in tabs.Items)
                    {
                        tabItem.ApplyTemplate();
                        var chrome=(Border)tabItem.Template.FindName("TabChrome",tabItem);
                        var background=(SolidColorBrush)(tabItem.IsSelected?chrome.Background:application.FindResource("RaisedBackgroundColor"));
                        var label=ThemeConsistencyValidation.Visuals(tabItem).OfType<TextBlock>().Single(t=>t.Text==tabItem.Header.ToString());
                        Assert.IsTrue(label.ActualWidth>0 && label.ActualHeight>0,"Both tab labels must be visible.");
                        Assert.IsTrue(AppThemeCatalog.Contrast(((SolidColorBrush)label.Foreground).Color,background.Color)>=4.5,
                            $"{theme}: {tabItem.Header} must be readable in both selection states.");
                    }
                    string path=System.IO.Path.Combine(directory,$"Controller-info-{theme}-{tab}.png");
                    ThemeConsistencyValidation.Save(content,path,820,740);files.Add(path);
                }
                window.Close();
                // Measure the real production card template inside a themed ListView.
                // Previously the dark ListView template hard-coded only right padding.
                var source=XDocument.Load(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(SourcePath()),"..","DS4Windows","DS4Forms","MainWindow.xaml"));
                XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";
                var listMarkup=source.Descendants().Single(e=>(string)e.Attribute(x+"Name")=="controllerLV");
                var padding=(Thickness)new ThicknessConverter().ConvertFromInvariantString((string)listMarkup.Attribute("Padding"));
                var list=new ListView { Width=760,Height=180,Padding=padding,ItemContainerStyle=(Style)application.FindResource("BridgeControllerCardItemStyle"),HorizontalContentAlignment=HorizontalAlignment.Stretch };
                ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);
                VirtualizingPanel.SetIsVirtualizing(list,false);
                list.Items.Add("Selected controller");list.SelectedIndex=0;list.ApplyTemplate();
                var layoutHost=new Window { Content=list,Width=760,Height=180,Left=-10000,Top=-10000,ShowActivated=false,ShowInTaskbar=false,WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize };
                layoutHost.Show();
                list.UpdateLayout();
                var item=(ListViewItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.IsNotNull(item,"Controller card must be realized in "+theme);
                item.ApplyTemplate();
                var card=(Border)item.Template.FindName("Card",item);
                Rect bounds=card.TransformToAncestor(list).TransformBounds(new Rect(0,0,card.ActualWidth,card.ActualHeight));
                Assert.IsTrue(bounds.Left>=8,"Selection outline must be inset from the container.");
                Assert.AreEqual(bounds.Left,list.ActualWidth-bounds.Right,1.0,"Controller card side margins must match in "+theme);
                layoutHost.Close();
            }
        }
        finally {application.Resources.MergedDictionaries.Clear();foreach(var resource in original)application.Resources.MergedDictionaries.Add(resource);application.ShutdownMode=shutdown;}
    }
    private static string SourcePath([System.Runtime.CompilerServices.CallerFilePath]string source="")=>source;
}
