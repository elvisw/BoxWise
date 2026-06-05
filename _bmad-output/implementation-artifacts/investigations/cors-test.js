async (page) => {
  const API_KEY = 'ark-xxx-REDACTED';
  const API_URL = 'https://ark.cn-beijing.volces.com/api/v3/chat/completions';

  // Test 1: Simple text request
  console.log('=== TEST 1: Text-only request ===');
  try {
    const res = await page.evaluate(async ({url, key}) => {
      const r = await fetch(url, {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': 'Bearer ' + key
        },
        body: JSON.stringify({
          model: 'doubao-seed-2-0-pro-260215',
          messages: [{role: 'user', content: 'say hi'}],
          max_tokens: 10
        })
      });
      return {
        status: r.status,
        statusText: r.statusText,
        type: r.type,
        headers: [...r.headers.entries()].filter(([k]) => k.startsWith('access-control')),
        body: (await r.text()).substring(0, 300)
      };
    }, {url: API_URL, key: API_KEY});

    console.log('Result:', JSON.stringify(res, null, 2));

    if (res.type === 'cors' && res.status === 200) {
      console.log('✅ CORS SUCCESS - cross-origin request allowed!');
    } else if (res.type === 'opaque') {
      console.log('❌ CORS FAILED - opaque response (no CORS headers)');
    } else if (res.status === 0) {
      console.log('❌ CORS BLOCKED - request was blocked by browser');
    } else {
      console.log('⚠️  Status:', res.status, 'Type:', res.type);
    }
  } catch(e) {
    console.log('ERROR:', e.message);
  }

  // Test 2: Preflight OPTIONS
  console.log('\n=== TEST 2: Preflight OPTIONS ===');
  try {
    const optRes = await page.evaluate(async ({url, key}) => {
      const r = await fetch(url, {
        method: 'OPTIONS',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': 'Bearer ' + key
        }
      });
      return {
        status: r.status,
        headers: [...r.headers.entries()].filter(([k]) => k.startsWith('access-control')),
      };
    }, {url: API_URL, key: API_KEY});
    console.log('OPTIONS:', JSON.stringify(optRes, null, 2));
  } catch(e) {
    console.log('OPTIONS ERROR:', e.message);
  }
}
