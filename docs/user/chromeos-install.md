# Installing FreeX on ChromeOS

FreeX is not a Chrome extension or a web app. ChromeOS support uses the built-in Linux development environment (Crostini) to run the existing self-contained Linux Avalonia build.

This path applies to FreeX, FreeW, and FreeP tester releases.

## Requirements

- A Chromebook that supports **Linux development environment**.
- Enough storage for the Linux container and the extracted application.
- An Intel/AMD Chromebook with the `linux-x64` package, or an ARM64 Chromebook with the `linux-arm64` package.
- A ChromeOS version and Linux container that expose Linux GUI applications.

## Enable Linux

1. Open **Settings**.
2. Open **Advanced > Developers > Linux development environment**.
3. Choose **Turn on** and complete the setup.
4. Open the **Terminal** app.

## Install A Tester Build

1. In Chrome, open the matching app's GitHub tester release page and download both the Linux archive and its adjacent `.sha256` file.
2. In the ChromeOS Files app, right-click the downloaded files and choose **Share with Linux**.
3. In Terminal, install the basic Linux GUI and archive dependencies:

   ```bash
   sudo apt update
   sudo apt install unzip libfontconfig1 libfreetype6 libx11-6 libxcb1
   ```

4. Verify the download. Use `linux-x64` for Intel/AMD or `linux-arm64` for ARM64:

   ```bash
   cd ~/Downloads
   sha256sum -c <App>-v<version>-linux-<architecture>.zip.sha256
   ```

5. Extract and launch the app:

   ```bash
   mkdir -p ~/Apps/<App>
   unzip <App>-v<version>-linux-<architecture>.zip -d ~/Apps/<App>
   chmod +x ~/Apps/<App>/<App>
   ~/Apps/<App>/<App>
   ```

The app should open as a Linux window alongside ChromeOS applications. Keep the extracted directory intact and close the app before replacing it during an update.

## Chromebook Files

To open a workbook stored in ChromeOS storage, use the Files app's **Share with Linux** action on its containing folder, then open the file from the corresponding `/mnt/chromeos/` path in the Linux container. Alternatively, copy the workbook into the Linux **Files** area before opening it.

Application data, settings, and diagnostics remain inside the Linux container. They are not automatically synchronized with ChromeOS or Google Drive.

## Troubleshooting

- **Wrong architecture:** run `dpkg --print-architecture`. `amd64` maps to `linux-x64`; `arm64` maps to `linux-arm64`.
- **No window appears:** confirm that Linux apps launch normally from the ChromeOS Terminal and restart the Linux environment from ChromeOS settings.
- **Missing native library:** rerun the `apt install` command and restart the app.
- **File is not visible:** share the folder with Linux in the ChromeOS Files app, or copy the file into the Linux Files area.
- **Checksum failure:** delete the download and download the archive and adjacent checksum again from the same GitHub release page.

This is a Linux-container compatibility path. It does not provide a native ChromeOS package, Chrome Web Store listing, Chromium tab runtime, offline PWA, or Android APK.
