using System.Net;
using System.Text;

namespace BusinessOS.Api.Payments;

public static class FreeTestingCheckoutPageEndpoints
{
    public static IEndpointRouteBuilder MapFreeTestingCheckoutPageEndpoints(
        this IEndpointRouteBuilder app)
    {
        app.MapGet("/testing/free-checkout", (
            IConfiguration configuration,
            IHostEnvironment environment) =>
        {
            if (!IsFreeTestingMode(configuration, environment))
                return Results.NotFound();

            return Results.Content(Html(), "text/html", Encoding.UTF8);
        });

        return app;
    }

    private static bool IsFreeTestingMode(
        IConfiguration configuration,
        IHostEnvironment environment) =>
        !environment.IsProduction() &&
        string.Equals(configuration["BusinessOS:DeploymentMode"], "FreeTesting",
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(configuration["BusinessOS:Payments:Mode"], "RazorpayTestPending",
            StringComparison.OrdinalIgnoreCase);

    private static string Html() =>
        """
        <!doctype html>
        <html lang="en">
        <head>
          <meta charset="utf-8" />
          <meta name="viewport" content="width=device-width, initial-scale=1" />
          <title>BusinessOS Free Staging Checkout Test</title>
          <style>
            :root { color-scheme: light dark; font-family: Inter, Arial, sans-serif; }
            body { margin: 0; background: #0f172a; color: #e5e7eb; }
            main { max-width: 980px; margin: 0 auto; padding: 28px; }
            .card { background: #111827; border: 1px solid #334155; border-radius: 18px; padding: 20px; margin: 16px 0; }
            input, button { font: inherit; border-radius: 10px; padding: 12px; border: 1px solid #475569; }
            input { width: 100%; box-sizing: border-box; background: #020617; color: #f8fafc; margin: 8px 0 14px; }
            button { cursor: pointer; background: #2563eb; color: white; border: 0; margin-right: 8px; margin-top: 8px; }
            pre { white-space: pre-wrap; background: #020617; padding: 14px; border-radius: 12px; overflow: auto; }
            .ok { color: #86efac; } .warn { color: #fde68a; }
            .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 12px; }
            .pill { display:inline-block; padding: 6px 10px; border-radius: 999px; background:#1e293b; }
          </style>
        </head>
        <body>
          <main>
            <h1>BusinessOS Free Staging Checkout Test</h1>
            <p class="warn">Testing only. No live Razorpay, no paid DB, no production payment.</p>
            <div class="card">
              <label>Staging bearer token</label>
              <input id="token" type="password" autocomplete="off" placeholder="Paste staging token from Render only for testing" />
              <button onclick="runHealth()">Check Health</button>
              <button onclick="runFullFlow()">Run Full Purchase Flow</button>
              <button onclick="setupAutoPay()">Setup AutoPay for Last Subscription</button>
              <button onclick="cancelLast()">Cancel Last Subscription at Period End</button>
            </div>
            <div class="grid">
              <div class="card"><b>Plan</b><br><span class="pill">AI_REPAIR</span></div>
              <div class="card"><b>Amount</b><br><span class="pill">₹29,999</span></div>
              <div class="card"><b>Seats</b><br><span class="pill">10 admin + 10 field</span></div>
            </div>
            <div class="card"><h3>Output</h3><pre id="out">Ready.</pre></div>
          </main>
          <script>
            const out = document.getElementById('out');
            const write = (label, data) => {
              out.textContent += `\n\n${label}\n${JSON.stringify(data, null, 2)}`;
            };
            const token = () => document.getElementById('token').value.trim();
            let lastSubscriptionId = null;
            async function request(path, options = {}) {
              const headers = { 'Content-Type': 'application/json', ...(options.headers || {}) };
              if (token()) headers.Authorization = `Bearer ${token()}`;
              const res = await fetch(path, { ...options, headers });
              const text = await res.text();
              let body;
              try { body = text ? JSON.parse(text) : null; } catch { body = text; }
              if (!res.ok) throw new Error(`${path} failed: ${res.status} ${text}`);
              return body;
            }
            async function runHealth() {
              out.textContent = 'Running health checks...';
              write('GET /health', await request('/health'));
              write('GET /health/ready', await request('/health/ready'));
            }
            async function runFullFlow() {
              if (!token()) { alert('Paste staging bearer token first.'); return; }
              out.textContent = 'Running full free-staging purchase flow...';
              await runHealth();
              const payload = {
                organisationId: '11111111-1111-1111-1111-111111111111',
                productCode: 'AI_REPAIR', planId: null, planVersionId: null,
                planVersionNumber: 1, amount: 29999, currencyCode: 'INR', termMonths: 12,
                desktopDeviceLimit: 1, locationLimit: 1,
                webAdminSeats: 10, fieldStaffSeats: 10, multiLocationCloud: true
              };
              const checkout = await request('/api/commerce/checkout/initial', {
                method: 'POST', body: JSON.stringify(payload)
              });
              write('POST /api/commerce/checkout/initial', checkout);
              const capture = await request(`/api/testing/payments/razorpay/orders/${checkout.razorpayOrderId}/capture`, {
                method: 'POST', body: JSON.stringify({ paymentId: null, capturedAtUtc: null })
              });
              write('POST /api/testing/payments/.../capture', capture);
              const subId = capture.initialActivation.subscriptionId;
              lastSubscriptionId = subId;
              const subscription = await request(`/api/commerce/subscriptions/${subId}`);
              write('GET /api/commerce/subscriptions/{id}', subscription);
              const entitlement = await request(`/api/commerce/subscriptions/${subId}/entitlement`);
              write('GET /api/commerce/subscriptions/{id}/entitlement', entitlement);
              const admin = await request('/api/commerce/admin/status');
              write('GET /api/commerce/admin/status', admin);
              out.textContent += '\n\nFull free-staging purchase flow completed.';
            }
            async function cancelLast() {
              if (!token()) { alert('Paste staging bearer token first.'); return; }
              if (!lastSubscriptionId) { alert('Run the purchase flow first.'); return; }
              const cancelled = await request(
                `/api/commerce/subscriptions/${lastSubscriptionId}/cancel-at-period-end`,
                { method: 'POST' });
              write('POST /api/commerce/subscriptions/{id}/cancel-at-period-end', cancelled);
            }
          </script>
        </body>
        </html>
        """;
}
