// Mints dev JWT access tokens for testing analytics-service locally.
// Same contract as core-service: HS256 with ACCESS_SECRET, no issuer/audience,
// raw claim names. Writes scripts/.dev-tokens.env (gitignored).
const crypto = require('crypto');
const fs = require('fs');
const path = require('path');

const secret = process.env.ACCESS_SECRET;
if (!secret) {
    console.error('export ACCESS_SECRET first (e.g. `set -a && source .env && set +a`)');
    process.exit(1);
}

const b64 = (o) => Buffer.from(JSON.stringify(o)).toString('base64url');
const header = b64({alg: 'HS256', typ: 'JWT'});

function sign(payload) {
    const body = b64({...payload, exp: Math.floor(Date.now() / 1000) + 3600});
    const sig = crypto.createHmac('sha256', secret)
        .update(`${header}.${body}`)
        .digest('base64url');
    return `${header}.${body}.${sig}`;
}

const tokens = {
    admin: sign({userId: 1, email: 'admin@crave.test', role: 'system_admin'}),
    owner42: sign({userId: 2, email: 'owner@crave.test', role: 'restaurant_user',
        restaurantId: 42, restaurantRole: 'owner'}),
    otherOwner7: sign({userId: 3, email: 'other@crave.test', role: 'restaurant_user',
        restaurantId: 7, restaurantRole: 'owner'}),
    staff42: sign({userId: 4, email: 'staff@crave.test', role: 'restaurant_user',
        restaurantId: 42, restaurantRole: 'staff'}),
};

const out = path.join(__dirname, '.dev-tokens.env');
fs.writeFileSync(out, Object.entries(tokens).map(([k, v]) => `${k}=${v}`).join('\n') + '\n');
console.log(`tokens written to ${out} (valid 1h)`);
