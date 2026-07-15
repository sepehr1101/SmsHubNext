namespace SmsHubNext.Features.Landing;

public static class ServiceLandingPage
{
    private const string DocumentationButtonPlaceholder = "<!-- OPENAPI_DOCUMENTATION_BUTTON -->";

    private const string DocumentationButton =
        "<a class=\"button\" href=\"/scalar/v1\">مستندات API</a>";

    public static IResult Render(bool openApiEnabled) =>
        Results.Content(GetHtml(openApiEnabled), "text/html; charset=utf-8");

    public static string GetHtml(bool openApiEnabled)
    {
        string documentationButton = openApiEnabled ? DocumentationButton : string.Empty;
        return Html.Replace(
            DocumentationButtonPlaceholder,
            documentationButton,
            StringComparison.Ordinal);
    }

    private const string Html =
        """
        <!doctype html>
        <html lang="fa" dir="rtl">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>SmsHubNext</title>
          <style>
            :root {
              color-scheme: light;
              --ink: #17233c;
              --muted: #667085;
              --line: #dfe5ee;
              --surface: rgba(255, 255, 255, .9);
              --primary: #155eef;
              --primary-dark: #004eeb;
              --success: #067647;
              --success-bg: #ecfdf3;
            }
            * { box-sizing: border-box; }
            body {
              margin: 0;
              min-height: 100vh;
              font-family: Tahoma, "Segoe UI", sans-serif;
              color: var(--ink);
              background:
                radial-gradient(circle at 12% 8%, rgba(21, 94, 239, .18), transparent 28rem),
                radial-gradient(circle at 88% 92%, rgba(13, 148, 136, .14), transparent 26rem),
                #f6f8fc;
            }
            main { width: min(1060px, calc(100% - 32px)); margin: 0 auto; padding: 56px 0 32px; }
            .hero, .panel {
              background: var(--surface);
              border: 1px solid rgba(223, 229, 238, .9);
              box-shadow: 0 24px 70px rgba(24, 34, 56, .09);
              backdrop-filter: blur(12px);
            }
            .hero { border-radius: 28px; padding: clamp(28px, 6vw, 64px); overflow: hidden; position: relative; }
            .hero::after {
              content: "SMS";
              position: absolute;
              direction: ltr;
              left: 28px;
              bottom: -35px;
              color: rgba(21, 94, 239, .045);
              font-size: clamp(100px, 20vw, 220px);
              font-weight: 800;
              letter-spacing: -12px;
              pointer-events: none;
            }
            .brand { display: flex; align-items: center; gap: 14px; margin-bottom: 38px; }
            .logo {
              width: 52px;
              height: 52px;
              display: grid;
              place-items: center;
              border-radius: 16px;
              color: white;
              background: linear-gradient(145deg, var(--primary), #0e9384);
              box-shadow: 0 12px 30px rgba(21, 94, 239, .25);
              font-weight: 800;
              direction: ltr;
            }
            .brand strong { display: block; font-size: 20px; direction: ltr; text-align: right; }
            .brand small { color: var(--muted); }
            .status {
              display: inline-flex;
              align-items: center;
              gap: 9px;
              border-radius: 999px;
              padding: 8px 12px;
              color: var(--success);
              background: var(--success-bg);
              border: 1px solid #abefc6;
              font-size: 13px;
              font-weight: 700;
            }
            .dot { width: 9px; height: 9px; border-radius: 50%; background: #17b26a; box-shadow: 0 0 0 5px rgba(23,178,106,.12); }
            h1 { margin: 20px 0 12px; font-size: clamp(32px, 5vw, 54px); line-height: 1.25; letter-spacing: -1px; }
            .lead { max-width: 680px; margin: 0; color: var(--muted); font-size: 17px; line-height: 2; }
            .actions { display: flex; flex-wrap: wrap; gap: 12px; margin-top: 30px; position: relative; z-index: 1; }
            .button {
              display: inline-flex;
              align-items: center;
              justify-content: center;
              min-height: 46px;
              padding: 0 20px;
              border-radius: 12px;
              border: 1px solid var(--line);
              color: var(--ink);
              background: white;
              text-decoration: none;
              font-weight: 700;
              transition: transform .15s ease, box-shadow .15s ease;
            }
            .button:hover { transform: translateY(-2px); box-shadow: 0 10px 24px rgba(24,34,56,.1); }
            .button.primary { color: white; border-color: var(--primary); background: linear-gradient(135deg, var(--primary), var(--primary-dark)); }
            .panel { margin-top: 22px; border-radius: 22px; padding: 28px; }
            .panel h2 { margin: 0 0 8px; font-size: 19px; }
            .panel > p { margin: 0 0 22px; color: var(--muted); line-height: 1.8; }
            .links { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 12px; }
            .link {
              display: block;
              min-height: 92px;
              padding: 17px;
              border-radius: 15px;
              border: 1px solid var(--line);
              background: #fff;
              color: var(--ink);
              text-decoration: none;
            }
            .link:hover { border-color: #84adff; background: #f9fbff; }
            .link strong { display: block; margin-bottom: 8px; }
            .link code { color: var(--muted); font-family: Consolas, monospace; font-size: 12px; direction: ltr; display: block; text-align: left; }
            footer { padding: 24px 4px 0; color: var(--muted); font-size: 12px; text-align: center; }
            @media (max-width: 760px) {
              main { padding-top: 20px; }
              .hero { border-radius: 20px; }
              .links { grid-template-columns: 1fr; }
              .button { width: 100%; }
            }
          </style>
        </head>
        <body>
          <main>
            <section class="hero">
              <div class="brand">
                <div class="logo">SMS</div>
                <div><strong>SmsHubNext</strong><small>سامانه ارسال و حسابداری پیامک</small></div>
              </div>
              <div id="status" class="status"><span class="dot"></span><span>سرویس در حال اجراست</span></div>
              <h1>نصب با موفقیت انجام شده است.</h1>
              <p class="lead">هسته SmsHubNext فعال است و درخواست‌های API و پردازش‌های پس‌زمینه را می‌پذیرد. از گزینه‌های زیر برای بررسی سلامت یا مشاهده اطلاعات فنی سرویس استفاده کنید.</p>
              <div class="actions">
                <a class="button primary" href="/health">بررسی سلامت سرویس</a>
                <a class="button" href="/service-info">مشاهده اطلاعات فنی</a>
                <!-- OPENAPI_DOCUMENTATION_BUTTON -->
              </div>
            </section>

            <section class="panel">
              <h2>دسترسی‌های سریع</h2>
              <p>این مسیرها پاسخ JSON برمی‌گردانند و برای بررسی اولیه یا اتصال کلاینت API مناسب‌اند.</p>
              <div class="links">
                <a class="link" href="/reference-data/providers"><strong>ارائه‌دهندگان</strong><code>/reference-data/providers</code></a>
                <a class="link" href="/reference-data/sender-lines"><strong>خطوط ارسال</strong><code>/reference-data/sender-lines</code></a>
                <a class="link" href="/reference-data/message-types"><strong>انواع پیام</strong><code>/reference-data/message-types</code></a>
                <a class="link" href="/customers"><strong>مشتریان</strong><code>/customers</code></a>
                <a class="link" href="/tariffs"><strong>تعرفه‌ها</strong><code>/tariffs</code></a>
                <a class="link" href="/dispatch/operations/summary"><strong>وضعیت پردازش</strong><code>/dispatch/operations/summary</code></a>
              </div>
            </section>
            <footer>SmsHubNext · Windows + IIS deployment</footer>
          </main>
          <script>
            fetch('/health', { cache: 'no-store' })
              .then(function (response) {
                if (!response.ok) throw new Error('unhealthy');
                document.querySelector('#status span:last-child').textContent = 'سرویس و دیتابیس آماده‌اند';
              })
              .catch(function () {
                var status = document.getElementById('status');
                status.style.color = '#b54708';
                status.style.background = '#fffaeb';
                status.style.borderColor = '#fedf89';
                status.querySelector('.dot').style.background = '#f79009';
                status.querySelector('span:last-child').textContent = 'سرویس اجراست؛ سلامت دیتابیس را بررسی کنید';
              });
          </script>
        </body>
        </html>
        """;
}
