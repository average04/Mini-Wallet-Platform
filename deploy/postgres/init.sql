-- Disposable local credentials. Each service owns a separate database and role.
CREATE USER users_app WITH PASSWORD 'local-users';
CREATE DATABASE users_db OWNER users_app;
CREATE USER wallet_app WITH PASSWORD 'local-wallet';
CREATE DATABASE wallet_db OWNER wallet_app;
CREATE USER keycloak_app WITH PASSWORD 'local-keycloak';
CREATE DATABASE keycloak_db OWNER keycloak_app;
REVOKE CONNECT ON DATABASE users_db FROM PUBLIC;
GRANT CONNECT ON DATABASE users_db TO users_app;
REVOKE CONNECT ON DATABASE wallet_db FROM PUBLIC;
GRANT CONNECT ON DATABASE wallet_db TO wallet_app;
REVOKE CONNECT ON DATABASE keycloak_db FROM PUBLIC;
GRANT CONNECT ON DATABASE keycloak_db TO keycloak_app;
