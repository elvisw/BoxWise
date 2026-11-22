#!/bin/bash
set -e

# 测试脚本：模拟 LlmClient 调用火山 API
IMG="$1"
if [ -z "$IMG" ]; then
    IMG=/opt/boxwise/data/2/original.jpg
fi

API_KEY="ark-xxx-REDACTED"
API_URL="https://ark.cn-beijing.volces.com/api/v3/chat/completions"
MODEL="doubao-seed-2-0-pro-260215"

echo "=== IMAGE INFO ==="
echo "Path: $IMG"
echo "Size: $(stat -c%s "$IMG") bytes ($(numfmt --to=iec $(stat -c%s "$IMG")))"

echo ""
echo "=== base64 ENCODING ==="
START_ENC=$(date +%s%N)
B64=$(base64 -w0 "$IMG")
END_ENC=$(date +%s%N)
ENC_MS=$(( (END_ENC - START_ENC) / 1000000 ))
B64SIZE=$(echo -n "$B64" | wc -c)
echo "base64 size: $B64SIZE bytes ($(numfmt --to=iec $B64SIZE))"
echo "Encoding took: ${ENC_MS}ms"
echo "Overhead: $(( (B64SIZE * 100) / $(stat -c%s "$IMG") - 100 ))%"

echo ""
echo "=== JSON BODY SIZE ==="
JSON_BODY=$(cat <<EOJSON
{
  "model": "$MODEL",
  "messages": [
    {
      "role": "user",
      "content": [
        {"type": "text", "text": "describe this image in one short sentence"},
        {"type": "image_url", "image_url": {"url": "data:image/jpeg;base64,$B64"}}
      ]
    }
  ],
  "max_tokens": 50
}
EOJSON
)
JSON_SIZE=$(echo -n "$JSON_BODY" | wc -c)
echo "JSON body size: $JSON_SIZE bytes ($(numfmt --to=iec $JSON_SIZE))"

echo ""
echo "=== CALLING API ==="
START=$(date +%s%N)
HTTP_CODE=$(curl -s -w '%{http_code}' -o /tmp/ai_response.json \
    -X POST "$API_URL" \
    -H "Content-Type: application/json" \
    -H "Authorization: Bearer $API_KEY" \
    --max-time 120 \
    --connect-timeout 30 \
    -d "$JSON_BODY" 2>/tmp/ai_error.log)
END=$(date +%s%N)
DURATION=$(( (END - START) / 1000 ))

echo ""
echo "=== RESULT ==="
echo "HTTP Code: $HTTP_CODE"
echo "Total time: ${DURATION}ms ($(( DURATION / 1000 ))s)"
echo ""
if [ "$HTTP_CODE" = "200" ]; then
    echo "Response body:"
    cat /tmp/ai_response.json | head -20
else
    echo "Error response:"
    cat /tmp/ai_response.json 2>/dev/null | head -20
    echo "Curl stderr:"
    cat /tmp/ai_error.log 2>/dev/null | head -10
fi

echo ""
echo "=== COMPARISON ==="
echo "App timeout setting: 60s (default, NOT overridden in production)"
echo "Actual API call time: ${DURATION}ms ($(( DURATION / 1000 ))s)"
if [ $(( DURATION / 1000 )) -gt 60 ]; then
    echo "VERDICT: EXCEEDS 60s timeout - WILL FAIL in production"
elif [ $(( DURATION / 1000 )) -gt 30 ]; then
    echo "VERDICT: Borderline - may timeout under load or with larger images"
else
    echo "VERDICT: Fast enough - timeout is not the bottleneck (check other causes)"
fi
