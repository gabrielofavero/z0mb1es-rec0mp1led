# Guarded desktop storage adaptation

For the pinned original DLL SHA-256 `7b819cb562826ae4d2592ff741877e76da0e57bfbe48192aabb79319fdb36823`, `GameplayPatcher` verifies the original `StorageMgr.Write` and `StorageMgr.Read` IL hashes, `887e2f79ecea6543a5ba56aecd08b4c6c82dcda8b790ee81a3e8e5a568249433` and `22a81b5a27a6ae99a20b31601d17b87e6fed1c576da135ea43065e0927de6d01`. It replaces only those two file boundary bodies with calls to `Port.Hooks.DesktopStorage`. Write still checks the original `TrialMgr.IsTrial` first.

Ten original `Read`/`Write` serializer parameters and their internal call references change from `IsolatedStorageFileStream` to `Stream`; the serializer bodies remain intact. The hook invokes those serializers in the original profile, player, custom order and writes the original version byte. The patch postcondition checks both hook calls and all ten signatures. Output is written to a new ignored `private/prepared` directory and recorded in its patch manifest.
