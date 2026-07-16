FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update -qq \
    && apt-get install -y -qq --no-install-recommends \
        dbus-x11 \
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
        libsm6 \
        libssl3 \
        libx11-6 \
        libx11-xcb1 \
        libxext6 \
        libxrender1 \
        novnc \
        openbox \
        scrot \
        shared-mime-info \
        websockify \
        wmctrl \
        x11vnc \
        x11-xserver-utils \
        xdg-utils \
        xdotool \
        xvfb \
        zlib1g \
    && rm -rf /var/lib/apt/lists/*

COPY entrypoint.sh /usr/local/bin/freex-linux-interactive
RUN chmod 0755 /usr/local/bin/freex-linux-interactive

EXPOSE 6080

ENTRYPOINT ["/usr/local/bin/freex-linux-interactive"]
