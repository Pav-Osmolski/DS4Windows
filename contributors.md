# Contributors

DS4Windows is built on years of community work. This page acknowledges the
project's lineage, incorporated contributions, and the reports and testing
that help maintain this fork.

## Project lineage and maintenance

| Contributor | Contribution |
| --- | --- |
| [Jays2Kings](https://github.com/Jays2Kings) | Original DS4Windows project. |
| [Travis Nickles (Ryochan7)](https://github.com/Ryochan7) | Long-running DS4Windows development and maintenance. |
| [Schmaldeo](https://github.com/schmaldeo) | Earlier DS4Windows fork development. |
| [hbashton](https://github.com/hbashton) | Upstream DS4Windows development, VIIPER integration, audio, feedback and interface work. |
| [Pawel Osmolski (Pav-Osmolski)](https://github.com/Pav-Osmolski) | Maintenance of this fork and its matching VIIPER and DS4Updater integration. |

## Incorporated code and fixes

| Contributor | Contribution |
| --- | --- |
| [meiameiameia](https://github.com/meiameiameia) | Profile-save and rename safety, virtual-controller removal, Bluetooth disconnect and touchpad-click fixes. |
| [petemess95](https://github.com/petemess95) | Reusing the profile XML serializer to reduce profile-switching overhead. |
| [Cameron Beeley (anagnorisis2peripeteia)](https://github.com/anagnorisis2peripeteia) | OpenRGB and HidHide contributions credited upstream, plus settings, macro and profile-migration regression coverage. |
| [Gabarsolon](https://github.com/Gabarsolon) and [Remus](https://github.com/Remus) | DSX game-mod compatibility contribution and associated VIIPER packaging fixes, recorded in upstream PR #86 and its commit authorship. |

Individual release entries and Git history retain further attribution. See
the [changelog](CHANGELOG.md), [upstream PR #86](https://github.com/hbashton/DS4Windows/pull/86)
and [GitHub contributor history](https://github.com/Pav-Osmolski/DS4Windows/graphs/contributors).

## Issue reports and testing

Thanks to the people who supplied reports, logs, recordings and follow-up
testing for this fork:

| Contributor | Report or investigation |
| --- | --- |
| [Jobima1st](https://github.com/Jobima1st) | [USB/IP compatibility (#4)](https://github.com/Pav-Osmolski/DS4Windows/issues/4). |
| [cyanojar](https://github.com/cyanojar) | [Axis Config scrolling and controller-readings layout (#19)](https://github.com/Pav-Osmolski/DS4Windows/issues/19). |
| [rudey123](https://github.com/rudey123) | [USB/IP migration and retained-device diagnostics (#20)](https://github.com/Pav-Osmolski/DS4Windows/issues/20). |
| [HRTMC](https://github.com/HRTMC) | [DS3 disconnect/reconnect and backend cleanup (#25)](https://github.com/Pav-Osmolski/DS4Windows/issues/25). |
| [Basfet](https://github.com/Basfet) | [DualSense profile lightbar behaviour (#28)](https://github.com/Pav-Osmolski/DS4Windows/issues/28). |
| [Masterload1975](https://github.com/Masterload1975) | [Special Actions profile saving (#33)](https://github.com/Pav-Osmolski/DS4Windows/issues/33). |

These credits acknowledge participation in the reports; they do not imply
that each person validated every fix or release.

## Nefarius and the controller ecosystem

Special thanks to [Nefarius](https://github.com/nefarius) for his foundational
contributions to Windows controller software. His [ViGEm project](https://github.com/nefarius/ViGEmBus)
powered virtual-controller output in earlier DS4Windows versions, and
[HidHide](https://github.com/nefarius/HidHide) helps prevent double input in
this fork by hiding physical controllers from games.

This fork now uses VIIPER for virtual-controller output, while continuing to
benefit from the wider ecosystem he helped build. If his work has helped you,
please consider [supporting his projects](https://docs.nefarius.at/Donations/).

## Earlier DS4Windows contributors

The following names are preserved from the inherited contributor list.
Travis Nickles (Ryochan7) is credited above. This historical list is not an
exhaustive record of every code, translation, documentation or testing contribution.

| Contributor | Contributor | Contributor | Contributor |
| --- | --- | --- | --- |
| Adamsw72 | Ahmed | AnessZurba | avsha114 |
| Axroxx | bernspedras | carloshbcabral | christranq |
| CrazyHPi | DandelionSprout | dante38 | Denellyne |
| DerGoogler | dezerving | dondrakon | dpearson2699 |
| DraakjeYoblama | eemijun | EnzoChaussivert | Flo082002 |
| firodj | Geroyuni | GreySole | Haamar |
| h1nnak | hpesoj | jdanders | jdfeng |
| jmg2k | joaopedrogomes | justalemon | jyc001 |
| Kamilczak020 | Kanuan | Кладовая | Korney Czukowski (czukowski) |
| kurtanr | Leonardo Melati (Leomelati) | major-sanyi | MakiseKurisu |
| markguleno | mayawei | mika-n | MikeCZ23 |
| nefarius | nelitow | Oliver-Noah | olokos |
| onuralpyilkin | PaulWentzel | peter9811 | Rajko Stojadinovic (rajkosto) |
| RedDevilus | RobbyCBennett | RokasKil | sabihoshi |
| Samzyre | SakifX9 | Sander0542 | shikulja |
| sitiom | SyaoranChang | sergejkiller | SteelShot |
| stryblt | sunnyqeen | taolatao147 | toshixm |
| tpneill | TRARS | trlef19 | V-Olk |
| wingfixer | xAdler | xLive | xrossb |
| Ygov | Yohoki | Yuki-nyan |  |

## Related projects and third-party credits

Thanks also to [Alia5 / Peter Repukat](https://github.com/Alia5), the VIIPER
contributors, and the maintainers of USB/IP, HidHide, FakerInput and the other
libraries and assets used here. Their project credits, licenses and component
notices are indexed in [NOTICE.txt](NOTICE.txt).
