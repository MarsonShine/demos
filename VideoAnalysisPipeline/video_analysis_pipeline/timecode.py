from __future__ import annotations


def format_timestamp(milliseconds: int) -> str:
    total_ms = max(0, int(milliseconds))
    hours, remainder = divmod(total_ms, 3_600_000)
    minutes, remainder = divmod(remainder, 60_000)
    seconds, millis = divmod(remainder, 1_000)
    return f"{hours:02d}:{minutes:02d}:{seconds:02d}.{millis:03d}"


def seconds_to_milliseconds(value: float) -> int:
    return int(round(value * 1000))


def ticks_to_milliseconds(value: int) -> int:
    return int(round(value / 10_000))


def natural_sort_key(value: str) -> list[object]:
    parts: list[object] = []
    current = ""
    digit_mode: bool | None = None

    for char in value:
        is_digit = char.isdigit()
        if digit_mode is None:
            digit_mode = is_digit
            current = char
            continue
        if is_digit == digit_mode:
            current += char
            continue
        parts.append(int(current) if digit_mode else current.lower())
        current = char
        digit_mode = is_digit

    if current:
        parts.append(int(current) if digit_mode else current.lower())

    return parts
