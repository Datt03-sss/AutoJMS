# Build the license server for Render's Docker runtime.
#
# Why Docker and not Render's native Node runtime: the existing Free-tier Web Service in
# Singapore is already configured with runtime = docker, and it has no Dockerfile in the repo
# to build from. Creating a fresh Blueprint-managed service instead would require a credit
# card on the account, so the cheaper move is to give the service the Dockerfile it expects.
#
# NOTE: repo root render.yaml still declares `runtime: node` with buildCommand/startCommand.
# That blueprint has not been applied (A1-b in docs/agent/BACKEND_BUILD_AND_VPS_DEPLOY_PLAN.vi.md
# is still open) so nothing conflicts today, but applying it as written would switch the
# service back to the Node runtime and ignore this file.
FROM node:22-alpine

WORKDIR /app

# Dependency manifests first so the install layer is reused whenever only source changes.
# package-lock.json is committed, which is what makes `npm ci` (not `npm install`) valid here.
COPY package*.json ./
# --omit=dev is a no-op today (the package has zero devDependencies - the test suite runs on
# node --test, which is built in) and stays as a guard for when one is added.
RUN npm ci --omit=dev

# Copy application source files
COPY . .

# No Express error-handling middleware exists in server.js, so an unhandled throw inside a
# route falls through to Express's default handler - and that handler puts the stack trace in
# the HTTP response body unless NODE_ENV is 'production'. On a public license endpoint that
# leaks absolute paths and internal structure. No application code reads NODE_ENV, so this
# only changes Express's own behaviour.
ENV NODE_ENV=production

# Documentation only: server.js binds `process.env.PORT || 3000` and Render injects PORT.
EXPOSE 3000

# Non-root user for security (the `node` user ships with the official image).
USER node

# Exec form on purpose: node stays PID 1 and therefore receives the SIGTERM that Render sends
# on every deploy and scale event, which is what the graceful shutdown at server.js:1742 hangs
# off. A shell form would put /bin/sh at PID 1 and swallow the signal.
CMD ["node", "server.js"]
