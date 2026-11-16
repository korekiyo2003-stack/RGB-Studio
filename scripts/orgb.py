"""OpenRGB SDK 客户端（协议 3），给 qlight 和调试用。

依据：OpenRGB 官方 SDK 文档（Documentation/OpenRGBSDK.md，v1.1，提交 8e16de2）。
后端是计划任务 “RGB Studio Memory Backend”，只监听 127.0.0.1:6743。

    python orgb.py dump            列出设备、模式、灯珠、当前颜色
    python orgb.py mode <名字> [--speed N] [--colors RRGGBB,...] [--dir N]   两条内存切硬件模式
    python orgb.py direct RRGGBB[,RRGGBB...]    两条内存切 Direct 并写颜色（按灯珠渐变）
"""
import json
import socket
import struct
import sys

HOST, PORT, PROTO = "127.0.0.1", 6743, 3
MAGIC = b"ORGB"


class Sdk:
    def __init__(self, host=HOST, port=PORT, timeout=3.0):
        self.s = socket.create_connection((host, port), timeout=timeout)
        self.send(0, 40, struct.pack("<I", PROTO))
        server = struct.unpack("<I", self.recv(0, 40))[0]
        if server < PROTO:
            raise IOError("server protocol %d < %d" % (server, PROTO))
        self.send(0, 50, b"qlight\0")

    def close(self):
        self.s.close()

    def send(self, dev, cmd, data=b""):
        self.s.sendall(MAGIC + struct.pack("<III", dev, cmd, len(data)) + data)

    def _read(self, n):
        buf = b""
        while len(buf) < n:
            part = self.s.recv(n - len(buf))
            if not part:
                raise IOError("connection closed")
            buf += part
        return buf

    def recv(self, dev, cmd):
        for _ in range(32):
            head = self._read(16)
            if head[:4] != MAGIC:
                raise IOError("bad magic")
            rdev, rcmd, size = struct.unpack("<III", head[4:])
            body = self._read(size)
            if rdev == dev and rcmd == cmd:
                return body
        raise IOError("no response")

    def count(self):
        self.send(0, 0)
        return struct.unpack("<I", self.recv(0, 0))[0]

    def device(self, dev):
        self.send(dev, 1, struct.pack("<I", PROTO))
        return parse_device(self.recv(dev, 1), dev)

    def devices(self):
        return [self.device(i) for i in range(self.count())]

    def set_custom(self, dev):
        self.send(dev, 1100)

    def update_leds(self, dev, colors):
        body = struct.pack("<H", len(colors)) + b"".join(struct.pack("<I", c) for c in colors)
        self.send(dev, 1050, struct.pack("<I", len(body) + 4) + body)

    def update_mode(self, dev, index, mode, save=False):
        body = struct.pack("<i", index) + pack_mode(mode)
        self.send(dev, 1102 if save else 1101, struct.pack("<I", len(body) + 4) + body)


class Reader:
    def __init__(self, data):
        self.d, self.p = data, 0

    def take(self, fmt):
        size = struct.calcsize(fmt)
        v = struct.unpack_from(fmt, self.d, self.p)
        self.p += size
        return v if len(v) > 1 else v[0]

    def string(self):
        n = self.take("<H")
        v = self.d[self.p:self.p + n]
        self.p += n
        return v.rstrip(b"\0").decode("utf-8", "replace")


MODE_FIELDS = ("value", "flags", "speed_min", "speed_max", "brightness_min", "brightness_max",
               "colors_min", "colors_max", "speed", "brightness", "direction", "color_mode")


def parse_device(data, dev):
    r = Reader(data)
    r.take("<I")
    d = {"id": dev, "type": r.take("<i"), "name": r.string(), "vendor": r.string(),
         "description": r.string(), "version": r.string(), "serial": r.string(),
         "location": r.string()}
    n_modes = r.take("<H")
    d["active"] = r.take("<i")
    d["modes"] = []
    for _ in range(n_modes):
        m = {"name": r.string()}
        vals = r.take("<iIIIIIIIIIII")
        m.update(zip(MODE_FIELDS, vals))
        m["colors"] = [r.take("<I") for _ in range(r.take("<H"))]
        d["modes"].append(m)
    d["zones"] = []
    for _ in range(r.take("<H")):
        z = {"name": r.string()}
        z["type"], z["min"], z["max"], z["count"] = r.take("<iIII")
        skip = r.take("<H")
        r.p += skip
        d["zones"].append(z)
    d["leds"] = []
    for _ in range(r.take("<H")):
        d["leds"].append(r.string())
        r.take("<I")
    d["colors"] = [r.take("<I") for _ in range(r.take("<H"))]
    return d


def pack_mode(m):
    name = m["name"].encode() + b"\0"
    out = struct.pack("<H", len(name)) + name
    out += struct.pack("<iIIIIIIIIIII", *[m[f] for f in MODE_FIELDS])
    out += struct.pack("<H", len(m["colors"])) + b"".join(struct.pack("<I", c) for c in m["colors"])
    return out


def rgb(hexstr):
    v = int(hexstr.lstrip("#"), 16)
    return ((v >> 16) & 255) | (v & 0xFF00) | ((v & 255) << 16)


def hexof(c):
    return "%02X%02X%02X" % (c & 255, (c >> 8) & 255, (c >> 16) & 255)


def gradient(points, n):
    if len(points) == 1 or n == 1:
        return [points[0]] * n
    out = []
    for i in range(n):
        pos = (len(points) - 1) * i / (n - 1)
        a = min(len(points) - 2, int(pos))
        f = pos - a
        c = 0
        for sh in (0, 8, 16):
            x, y = (points[a] >> sh) & 255, (points[a + 1] >> sh) & 255
            c |= int(round(x * (1 - f) + y * f)) << sh
        out.append(c)
    return out


def memory(sdk):
    return [d for d in sdk.devices() if d["type"] == 1 and d["name"] == "ENE DRAM" and d["colors"]]


def main(argv):
    sdk = Sdk()
    try:
        if argv[0] == "dump":
            for d in sdk.devices():
                d = dict(d)
                d["colors"] = [hexof(c) for c in d["colors"]]
                for m in d["modes"]:
                    m["colors"] = [hexof(c) for c in m["colors"]]
                    m["flags"] = "0x%X" % m["flags"]
                print(json.dumps(d, ensure_ascii=False))
        elif argv[0] == "direct":
            pts = [rgb(x) for x in argv[1].split(",")]
            for d in memory(sdk):
                sdk.set_custom(d["id"])
                sdk.update_leds(d["id"], gradient(pts, len(d["colors"])))
                print(d["location"], "direct", len(d["colors"]))
        elif argv[0] == "mode":
            opts = dict(zip(argv[2::2], argv[3::2]))
            for d in memory(sdk):
                idx = next(i for i, m in enumerate(d["modes"]) if m["name"].lower() == argv[1].lower())
                m = dict(d["modes"][idx])
                if "--speed" in opts:
                    m["speed"] = int(opts["--speed"])
                if "--dir" in opts:
                    m["direction"] = int(opts["--dir"])
                if "--colors" in opts:
                    pts = [rgb(x) for x in opts["--colors"].split(",")]
                    sdk.update_leds(d["id"], gradient(pts, len(d["colors"])))
                sdk.update_mode(d["id"], idx, m)
                print(d["location"], "mode", m["name"], "speed", m["speed"])
    finally:
        sdk.close()


if __name__ == "__main__":
    main(sys.argv[1:] or ["dump"])
