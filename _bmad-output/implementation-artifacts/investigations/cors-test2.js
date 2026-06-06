async (page) => {
  const result = await page.evaluate(async () => {
    const r = await fetch('https://ark.cn-beijing.volces.com/api/v3/chat/completions', {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
        'Authorization': 'Bearer ark-xxx-REDACTED'
      },
      body: JSON.stringify({
        model: 'doubao-seed-2-0-pro-260215',
        messages: [{role: 'user', content: 'hi'}],
        max_tokens: 5
      })
    });
    const text = await r.text();
    return JSON.stringify({
      ok: r.ok,
      status: r.status,
      statusText: r.statusText,
      type: r.type,
      redirected: r.redirected,
      corsHeaders: r.headers.get('access-control-allow-origin'),
      bodyPreview: text.substring(0, 200)
    });
  });
  await page.evaluate((r) => { document.title = 'CORS-TEST-RESULT:' + r; }, result);
  // Also set it as body text so we can eval it
  await page.evaluate((r) => { document.body.innerText = r; }, result);
  return result;
}
