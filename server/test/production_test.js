const BASE = 'https://xelth.com/AGI';

async function tests() {
  console.log('🔥 SMOKE TEST: xelth.com');

  // 1. HEALTH
  console.log('\n1️⃣ /HEALTH');
  try {
    const r = await fetch(BASE + '/HEALTH');
    console.log('   Status:', r.status, r.status === 200 ? '✅' : '❌');
    if (r.ok) console.log('   Data:', await r.json());
  } catch (e) { console.log('   Error:', e.message); }

  // 2. DOWNLOAD & TOKEN
  console.log('\n2️⃣ /DOWNLOAD/CLIENT');
  try {
    const r = await fetch(BASE + '/DOWNLOAD/CLIENT');
    if (r.ok) {
      const buf = Buffer.from(await r.arrayBuffer());
      const sig = Buffer.from('x1_', 'utf16le');
      const idx = buf.indexOf(sig);
      console.log('   Downloaded:', (buf.length / 1024 / 1024).toFixed(1), 'MB');
      console.log('   Token found:', idx !== -1 ? '✅' : '❌');
      if (idx !== -1) {
        const token = buf.subarray(idx, idx + 100).toString().replace(/\0/g, '').trim();
        console.log('   Token:', token.substring(0, 25) + '...');

        // 3. TEST AUTH
        console.log('\n3️⃣ Test Auth with token');
        const r2 = await fetch(BASE + '/DECIDE', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json', 'Authorization': 'Bearer ' + token },
          body: JSON.stringify({ ClientId: 'smoke_test', Task: 'test', State: { WindowTitle: 'Test', ProcessName: 'test', Elements: [] } })
        });
        console.log('   Status:', r2.status, r2.status === 200 ? '✅' : '❌');
        if (r2.ok) {
          const d = await r2.json();
          console.log('   Response:', d.Success ? 'Success' : d.Error || 'unknown');
        }
      }
    }
  } catch (e) { console.log('   Error:', e.message); }

  console.log('\n🏁 DONE');
}
tests();
