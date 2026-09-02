# Stage 1: Build
# Angular 21 requires Node 22+
FROM node:22-alpine AS build
WORKDIR /app

# Copy manifests first to maximize layer cache reuse
COPY package.json package-lock.json .npmrc ./
RUN npm ci

# Copy workspace source (node_modules excluded via .dockerignore)
COPY . .

RUN npx ng build host --configuration=production

# Stage 2: Serve with Nginx
FROM nginx:alpine
COPY --from=build /app/dist/host/browser /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
