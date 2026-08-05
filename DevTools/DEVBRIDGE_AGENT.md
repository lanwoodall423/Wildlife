# Wildlife Dev Bridge Workflow

The provider is part of the owner-controlled `Herds.dll`; no adapter DLL is distributed. Build the gameplay module, regenerate the loaded-module manifest, and validate its exact identity, MVID, length, and hash. Discover the live bridge and query `Lan.Wildlife` context before testing. A Herds change requires a full restart, followed by fresh context and discarded leases, cursors, handles, and cached state.
