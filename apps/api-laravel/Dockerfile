# HotelPulse Laravel API: one image for the web server, the outbox relay and
# the booking worker. Build context: apps/api-laravel.
#
#   docker run IMAGE web       Octane on FrankenPHP, listens on $PORT (8080)
#   docker run IMAGE relay     outbox relay
#   docker run IMAGE consume   booking worker
#   docker run IMAGE all       all three under supervisord (Render free plan)
#   docker run IMAGE migrate   apply migrations and exit
#
# PHP 8.5.x patch releases arrive with a rebuild; moving to 8.6 is a manual
# change of the tag below after reading its changelog.
FROM dunglas/frankenphp:php8.5 AS base
RUN install-php-extensions pdo_pgsql redis pcntl sockets intl bcmath opcache zip \
    && apt-get update \
    && apt-get install -y --no-install-recommends supervisor \
    && rm -rf /var/lib/apt/lists/*
COPY docker/php.ini /usr/local/etc/php/conf.d/zz-hotelpulse.ini

FROM base AS vendor
COPY --from=composer:2 /usr/bin/composer /usr/bin/composer
WORKDIR /app
COPY composer.json composer.lock ./
RUN composer install --no-dev --no-scripts --no-autoloader --prefer-dist --no-interaction --no-progress
COPY . .
RUN composer dump-autoload --optimize --classmap-authoritative --no-dev \
    && php artisan package:discover --ansi

FROM base
ENV APP_ENV=production \
    APP_DEBUG=false \
    LOG_CHANNEL=stderr_json \
    OCTANE_SERVER=frankenphp \
    PORT=8080 \
    XDG_CONFIG_HOME=/tmp/caddy-config \
    XDG_DATA_HOME=/tmp/caddy-data
RUN useradd --system --uid 10001 --home-dir /app hotelpulse
WORKDIR /app
COPY --from=vendor --chown=hotelpulse:hotelpulse /app /app
RUN mkdir -p storage/framework/cache storage/framework/views storage/logs bootstrap/cache \
    && chown -R hotelpulse:hotelpulse storage bootstrap/cache
COPY docker/supervisord.conf /etc/supervisor/hotelpulse.conf
COPY docker/entrypoint.sh /usr/local/bin/hotelpulse
RUN chmod 0755 /usr/local/bin/hotelpulse
USER hotelpulse
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --start-period=30s --retries=5 \
    CMD php -r 'exit(@file_get_contents("http://127.0.0.1:".getenv("PORT")."/health/live") === false ? 1 : 0);'
ENTRYPOINT ["hotelpulse"]
CMD ["web"]
