const { env } = require('process');

// Same as the VS template: points at the HTTPS port of the Server that SpaProxy launched.
const target = env.ASPNETCORE_HTTPS_PORT
  ? `https://localhost:${env.ASPNETCORE_HTTPS_PORT}`
  : env.ASPNETCORE_URLS
    ? env.ASPNETCORE_URLS.split(';')[0]
    : 'https://localhost:7180';

const PROXY_CONFIG = [
  {
    // /_internal → the anti-crawler trap must also reach the server in development
    context: ['/api', '/robots.txt', '/health', '/_internal'],
    target,
    secure: false,
    // Do not add X-Forwarded-* headers in development
    xfwd: false,
  },
];

module.exports = PROXY_CONFIG;
