FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update -qq \
    && apt-get install -y -qq --no-install-recommends \
        at-spi2-core \
        dbus-x11 \
        libglib2.0-bin \
        python3-dbus \
        python3-jsonschema \
        python3-gi \
        python3-pyatspi \
        espeak-ng \
        ffmpeg \
        fonts-dejavu \
        fonts-liberation \
        fonts-noto-cjk \
        imagemagick \
        libegl1 \
        libfontconfig1 \
        libgl1 \
        libgtk-3-0 \
        libice6 \
        libicu74 \
        libvlc-dev \
        libvlc5 \
        libvlccore9 \
        libsm6 \
        libssl3 \
        libx11-6 \
        libx11-xcb1 \
        libxext6 \
        libxrender1 \
        novnc \
        openbox \
        picom \
        scrot \
        shared-mime-info \
        websockify \
        wmctrl \
        vlc-plugin-base \
        vlc-plugin-video-output \
        xclip \
        x11vnc \
        x11-xserver-utils \
        xdg-utils \
        xdotool \
        xvfb \
        zlib1g \
    && rm -rf /var/lib/apt/lists/*

COPY entrypoint.sh /usr/local/bin/freex-linux-interactive
COPY refresh-after-vnc.sh /usr/local/bin/freex-refresh-after-vnc
COPY run-freep-accessibility-probe.sh /usr/local/bin/freep-accessibility-probe
RUN chmod 0755 \
        /usr/local/bin/freex-linux-interactive \
        /usr/local/bin/freex-refresh-after-vnc \
        /usr/local/bin/freep-accessibility-probe

EXPOSE 6080

ENTRYPOINT ["/usr/local/bin/freex-linux-interactive"]
