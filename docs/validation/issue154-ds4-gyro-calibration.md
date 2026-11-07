# DS4 gyro calibration regression checks

[Raine-07](https://github.com/Raine-07) reported inverted horizontal gyro passthrough on a QRD Spark N7 over Bluetooth. A Leadjoy Saber Plus worked with the same profile. Both controllers report Sony vendor and product IDs, so the fix validates the calibration data itself.

[nben's preview test](https://github.com/hbashton/DS4Windows/issues/154#issuecomment-6708700773) reproduced the inversion on a DS4-compatible Bluetooth controller and confirmed that the preview fixed it. The capture shows reversed yaw calibration endpoints. Raine-07's Spark N7 result is pending.

Gyro sensitivity uses the distances from the bias to the endpoints. Endpoint order therefore does not change the direction of motion. Failed feature reads, bad Bluetooth CRCs and implausible coefficients leave any previously accepted calibration in place. The Sony wireless adapter retains Bluetooth endpoint ordering for its report 0x02.

The regression suite replays the captured Bluetooth feature report through the calibration reader and checks both motion directions on all three axes in the virtual DS4 packet. Synthetic USB and Bluetooth cases compare ordinary calibration against the previous scaling and cover invalid reports and retries.

For a physical check, select **DualShock 4** as the emulated controller and **Passthru** as the gyro output. Use the same profile and game settings for each build. In Dolphin or Steam Input, test left/right, up/down and roll in both directions. Compare a known working controller before and after the change. Enable **Log > Verbose logging** before reconnecting if calibration diagnostics are needed.
