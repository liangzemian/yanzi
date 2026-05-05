create table if not exists app_release_channels (
  channel text primary key,
  version text not null,
  title text not null,
  notes text,
  download_url text not null,
  file_name text,
  download_code text,
  provider text not null default 'custom',
  sha256 text,
  published_at text not null,
  updated_at text not null,
  updated_by_user_id text,
  updated_by_username text,
  foreign key (updated_by_user_id) references users(user_id)
);
