// Disposable core-service double for local testing of analytics-service.
// Implements GET /api/internal/rbac/permissions?role= with api-key check.
// Grants: owner → analytics:read; staff → an unrelated permission;
// everyone else → [] (denied). NOT the real core-service.
require('http').createServer((req, res) => {
    const url = new URL(req.url, 'http://localhost');
    if (req.headers['api-key'] !== process.env.CORE_INTERNAL_API_KEY) {
        res.writeHead(401, {'content-type': 'application/json'});
        return res.end(JSON.stringify({error: 'Invalid api key'}));
    }
    if (url.pathname !== '/api/internal/rbac/permissions') {
        res.writeHead(404, {'content-type': 'application/json'});
        return res.end(JSON.stringify({error: 'Not found'}));
    }
    const role = url.searchParams.get('role') ?? '';
    const permissions = role === 'owner' ? ['analytics:read']
        : role === 'staff' ? ['core:product:read']
        : [];
    console.log(`rbac lookup role=${role}`);
    res.writeHead(200, {'content-type': 'application/json'});
    res.end(JSON.stringify({success: true, data: {role, permissions}}));
}).listen(3000, () => console.log('fake core listening on 3000'));
