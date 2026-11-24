# Anti Bot Starter

**ASP.NET Core 10 + Angular 22** starter with **layered anti-bot and anti-crawler protection**. It blocks scraping
tools, AI crawlers and fake search engines, catches crawlers with a trap, limits how many requests each visitor can
make, and protects the sign-in and registration forms with a CAPTCHA, a honeypot and single-use form tokens. Sign-in
answers the same whether or not the account exists, its emails cannot be used to flood other people, and it includes
account recovery and two-factor authentication.

> **No single measure blocks 100% of bots.** An advanced bot (real browser, residential IPs,
> CAPTCHA farms) can only be slowed down and made expensive. That is why layers are combined: each one
> filters what the previous one let through. What works best lives **outside** the app (WAF/CDN).

## How to use it

1. Open `https://localhost:63421/register` and register.
2. The confirmation email is printed in the **server console** (`[DEV EMAIL]`) a few seconds later.
3. Sign in and open **Products**. Under **Security** you can turn on two-factor authentication (QR code and
   recovery codes).
4. *Forgot your password?* and *Resend the confirmation email* are on the sign-in page.

## How to run it

**Requirements:** .NET SDK 10, Node.js 22.22.3+ or 24.15+, and SQL Server LocalDB (installed with Visual Studio).

Open `AntiBotStarter.slnx` in Visual Studio, set **AntiBotStarter.Server** as the startup project and press **F5**.
The application opens at `https://localhost:63421`.

From the command line:

```cmd
cd src\antibotstarter.client && npm install && cd ..\..
dotnet run --project src\AntiBotStarter.Server --launch-profile https
```

## Configuration

It needs no configuration to run in Development: the database is created and migrated on the first run, the CAPTCHA
(Turnstile) uses Cloudflare's test keys, and emails are printed in the server console instead of being sent.

The settings are in the `AntiBot` section of `appsettings.json`. Real keys and secrets never go in that file.

## Tests

```cmd
scripts\build-and-test.cmd
```

It runs the build, the .NET tests (with Docker containers for SQL Server, Redis and Mailpit), the Angular tests, the
Angular production build and the real-server smoke test. It needs Docker and Chrome.

## License and disclaimer

Licensed under the [Apache License 2.0](LICENSE): anyone may use, modify and distribute it, including commercially.

It is provided as an example, "as is", without warranty of any kind. Review and test it before using it in
production; the authors are not liable for any damage that results from its use.
