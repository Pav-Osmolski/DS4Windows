# DS3 disconnect cleanup and reconnection

Baseline: main `b6a4d076dee4881465ea08d988def5d8fe8777d3` (5.0.22).

[HRTMC's follow-up](https://github.com/Pav-Osmolski/DS4Windows/issues/25#issuecomment-6166095541)
confirms Stop/Start now works, but a Bluetooth DS3 (SXS) disconnect leaves
`WorkerStopRejected/None` after virtual Xbox 360 output was detached.

DS3's typed worker stop joins the input worker and then requires a successful
physical neutral output write. When the failed feature read has already
started removal, this last write can fail on the lost transport even though
the worker has exited. The connection is quarantined and its slot cannot
be reused. The reported warning does not distinguish a join timeout from
this output finalization failure, so hardware confirmation is still needed.

The failed-read removal path now records transport loss and skips its
redundant physical output write. The typed output finalizer accepts this
connection only after a zero-time join proves its exact input worker exited.
DS3 has no separate output worker. Connected service Stop still requires
the final physical neutral, and a running input worker still rejects cleanup.
An ordinary disconnecting flag does not bypass either check. Virtual output
neutralization and exact registry removal remain under their existing owner.

Tests use an actual DS3Device with its production input-loss handler, output
finalizer, base input join, lifecycle boundary and slot authority. A controlled
thread substitutes for hardware reads; the physical output hook rejects writes.
Before the finalizer fix, three of six regression cases failed: disconnect
cleanup, recovery after a join timeout, and exact slot retirement/replacement.
All six pass with the fix, alongside 50 existing worker, slot-authority and
DS3 feedback tests (56 total). Connected accepted/rejected neutral writes and
an unrelated disconnecting flag are covered as negative controls.

DirectInput development remains separate. No physical DS3 was available for
this validation; keep issue #25 open until the reporter checks the new build.
