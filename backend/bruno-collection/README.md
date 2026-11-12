# Crave — Bruno Endpoint Collection (analytics-service)

Covers analytics-service. Same conventions as the order-service collection.

| Service   | Env var         | Local value                |
| --------- | --------------- | -------------------------- |
| Analytics | `analyticsBase` | http://localhost:5000      |

## How to use

1. Open Bruno → **Open Collection** → select this folder (`backend/bruno-collection`).
2. Select the **Local** environment (dropdown at the top).
3. **Mint tokens** (terminal, once per hour — the app has no login endpoint; tokens
   are what core-service would issue):

   ```bash
   set -a && source .env && set +a
   node scripts/mint-dev-tokens.js
   ```

   Then copy each value from `scripts/.dev-tokens.env` into the matching **Local**
   env variable: `adminToken`, `owner42Token`, `staff42Token`, `otherOwner7Token`.
4. `00 Setup/01 Health` needs no token. `01 Restaurant Days/01 (admin)` works with
   only the token (system_admin bypasses RBAC — no core-service needed).
5. Role requests (`02–04`) resolve permissions against **core-service**. Without the
   real core running, start the stand-in double:

   ```bash
   node scripts/fake-core.js &
   ```

   `owner42` → 200 (has `analytics:read`), `staff42` (cookie auth) and
   `otherOwner7` → 403.
6. Seeded aggregate data for restaurant 42 exists from running
   `scripts/publish-test-event.sh` (consumer + handler path). Change `from`/`to`
   collection vars to match your event dates.
7. Validation variants (missing params, inverted range, too-wide range, unknown
   restaurant) are preconfigured — just click through them.
