alter table extensions add column publisher_user_id text;
alter table extensions add column publisher_username text;
alter table extensions add column published_at text;
alter table extensions add column is_published integer not null default 1;

create index if not exists idx_extensions_published_updated
  on extensions (is_published, updated_at desc);
