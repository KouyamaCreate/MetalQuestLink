# MetalQuestLink Editor

A self-contained Unity package for streaming Editor Play Mode to Meta Quest 3 / 3S. It includes a
prebuilt Apple Silicon OpenXR API layer and Quest APK; end users do not need CMake or Xcode.

Unity 2022.3 LTS is the resolver baseline. Package compatibility is verified on Unity 6000.2 and
6000.3; the 2022.3 local matrix still requires a licensed Editor in the verification environment.
Projects using newer XR Plug-in Management or OpenXR packages keep their compatible versions.

After installation, open **Window > MetalQuestLink**, click **Quick Setup (Project + Quest)**, and
press Play. The package registers its implicit OpenXR layer manifest when loaded.

See the repository [README](../README.md) for prerequisites, Meta XR Simulator setup, Gatekeeper
guidance, diagnostics, limitations, and the Japanese documentation link.
