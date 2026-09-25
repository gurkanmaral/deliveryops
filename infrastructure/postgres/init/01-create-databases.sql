SELECT 'CREATE DATABASE deliveryops_auth'
WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = 'deliveryops_auth')\gexec
