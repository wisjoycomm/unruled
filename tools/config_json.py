"""Strict JSON encoding for Unity data, preserving non-finite float values."""
import json
import math


def normalize(value):
    if isinstance(value, float) and not math.isfinite(value):
        return {"$float": "NaN" if math.isnan(value) else ("Infinity" if value > 0 else "-Infinity")}
    if isinstance(value, dict):
        return {key: normalize(item) for key, item in value.items()}
    if isinstance(value, (list, tuple, bytes, bytearray)):
        return [normalize(item) for item in value]
    return value


def dumps(value, **kwargs):
    return json.dumps(normalize(value), allow_nan=False, **kwargs)
