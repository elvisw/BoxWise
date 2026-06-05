#!/usr/bin/env python3
"""Measure upload speed to Volcano ARK API with different payload sizes."""
import subprocess, json, time, sys

API_URL = "https://ark.cn-beijing.volces.com/api/v3/chat/completions"
API_KEY = "ark-xxx-REDACTED"
MODEL = "doubao-seed-2-0-pro-260215"

def test_upload(payload_bytes):
    """Test how long it takes to upload a payload."""
    body = json.dumps({
        "model": MODEL,
        "messages": [{"role": "user", "content": "test"}],
        "max_tokens": 1,
        "padding": "x" * payload_bytes  # Simulate image data size
    })
    body_size = len(body.encode('utf-8'))

    start = time.time()
    result = subprocess.run([
        "curl", "-s", "-w", "%{http_code}|%{time_total}|%{speed_upload}",
        "-o", "/dev/null",
        "--max-time", "60",
        API_URL,
        "-H", "Content-Type: application/json",
        "-H", f"Authorization: Bearer {API_KEY}",
        "-d", body
    ], capture_output=True, text=True, timeout=65)
    elapsed = time.time() - start

    parts = result.stdout.strip().split("|")
    http_code = parts[0]
    total_time = parts[1]
    speed_upload = parts[2]

    print(f"  Payload: {body_size:,} bytes ({body_size/1024:.0f}KB)")
    print(f"  HTTP: {http_code}, Time: {total_time}s, Upload: {speed_upload}bps")
    print(f"  Effective upload rate: {body_size/float(total_time)/1024:.1f} KB/s")
    print(f"  Wall clock: {elapsed:.1f}s")

    # Calculate estimated time for 2.6MB
    if float(speed_upload) > 0:
        est_26mb = 2_678_099 / float(speed_upload)
        print(f"  Estimated time for 2.6MB: {est_26mb:.0f}s")
    print()
    return float(total_time)

print("=== UPLOAD SPEED TO VOLCANO ARK API ===")
print(f"Target: {API_URL}")
print()

# Test with increasing payload sizes
sizes = [1000, 10000, 50000, 100000, 500000]
for size in sizes:
    print(f"--- Testing {size}B padding ---")
    try:
        test_upload(size)
    except Exception as e:
        print(f"  FAILED: {e}")
        break

print("=== CONCLUSION ===")
print("Image base64 size: ~2.6MB (2,678,099 bytes)")
print("App timeout: 60s (default, NOT overridden in production)")
