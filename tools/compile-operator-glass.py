"""Compile the packaged WPF PS2 lens on Windows; not needed by app users."""
import ctypes
from pathlib import Path

root = Path(__file__).resolve().parents[1]
folder = root / "Roche_Scoreboard" / "Resources" / "Shaders"
source = (folder / "OperatorGlass.hlsl").read_bytes()
compiler = ctypes.WinDLL("d3dcompiler_47.dll")
compile_shader = compiler.D3DCompile
compile_shader.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_char_p,
    ctypes.c_void_p, ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p,
    ctypes.c_uint, ctypes.c_uint, ctypes.POINTER(ctypes.c_void_p), ctypes.POINTER(ctypes.c_void_p)]
compile_shader.restype = ctypes.c_long
code, errors = ctypes.c_void_p(), ctypes.c_void_p()


def read_blob(pointer):
    methods = ctypes.cast(pointer, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
    address = ctypes.WINFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(methods[3])(pointer)
    size = ctypes.WINFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)(methods[4])(pointer)
    return ctypes.string_at(address, size)


def release(pointer):
    if pointer:
        methods = ctypes.cast(pointer, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
        ctypes.WINFUNCTYPE(ctypes.c_ulong, ctypes.c_void_p)(methods[2])(pointer)


try:
    result = compile_shader(source, len(source), b"OperatorGlass.hlsl", None, None,
        b"main", b"ps_2_0", 0, 0, ctypes.byref(code), ctypes.byref(errors))
    if result < 0:
        raise RuntimeError(read_blob(errors).decode("utf-8", errors="replace") if errors else str(result))
    data = read_blob(code)
    (folder / "OperatorGlass.ps").write_bytes(data)
    print(f"Compiled packaged PS2 background lens: {len(data)} bytes")
finally:
    release(code)
    release(errors)
