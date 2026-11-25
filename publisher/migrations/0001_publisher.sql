PRAGMA foreign_keys=ON;
CREATE TABLE customers(id TEXT PRIMARY KEY,business TEXT NOT NULL,contact TEXT NOT NULL DEFAULT '',created TEXT NOT NULL,status TEXT NOT NULL DEFAULT 'active');
CREATE TABLE licenses(id TEXT PRIMARY KEY,customer_id TEXT NOT NULL REFERENCES customers(id),key_hash TEXT NOT NULL UNIQUE,
 edition TEXT NOT NULL,seats INTEGER NOT NULL CHECK(seats BETWEEN 1 AND 100),major INTEGER NOT NULL CHECK(major>0),
 updates_until TEXT NOT NULL,created TEXT NOT NULL,status TEXT NOT NULL DEFAULT 'active' CHECK(status IN ('active','suspended')),test INTEGER NOT NULL DEFAULT 1);
CREATE TABLE activations(id TEXT PRIMARY KEY,license_id TEXT NOT NULL REFERENCES licenses(id),device TEXT NOT NULL,label TEXT NOT NULL,
 created TEXT NOT NULL,last_seen TEXT NOT NULL,status TEXT NOT NULL DEFAULT 'active' CHECK(status IN ('active','revoked')),UNIQUE(license_id,device));
CREATE INDEX activations_license_status ON activations(license_id,status);
CREATE TRIGGER activation_seat_limit BEFORE INSERT ON activations WHEN NEW.status='active' BEGIN
 SELECT RAISE(ABORT,'SEAT_LIMIT') WHERE (SELECT count(*) FROM activations WHERE license_id=NEW.license_id AND status='active') >= (SELECT seats FROM licenses WHERE id=NEW.license_id);
END;
CREATE TRIGGER activation_seat_update BEFORE UPDATE OF status ON activations WHEN NEW.status='active' AND OLD.status<>'active' BEGIN
 SELECT RAISE(ABORT,'SEAT_LIMIT') WHERE (SELECT count(*) FROM activations WHERE license_id=NEW.license_id AND status='active') >= (SELECT seats FROM licenses WHERE id=NEW.license_id);
END;
CREATE TABLE transfers(id TEXT PRIMARY KEY,activation_id TEXT NOT NULL REFERENCES activations(id),created TEXT NOT NULL,status TEXT NOT NULL DEFAULT 'pending');
CREATE UNIQUE INDEX transfer_pending ON transfers(activation_id) WHERE status='pending';
CREATE TABLE releases(id TEXT PRIMARY KEY,version TEXT NOT NULL,edition TEXT NOT NULL,channel TEXT NOT NULL CHECK(channel IN ('development','testing','stable')),
 envelope TEXT NOT NULL,object_key TEXT NOT NULL,created TEXT NOT NULL,UNIQUE(version,edition,channel));
CREATE TABLE reports(id TEXT PRIMARY KEY,activation_id TEXT NOT NULL,version TEXT NOT NULL,category TEXT NOT NULL,summary TEXT NOT NULL,
 diagnostics TEXT NOT NULL,created TEXT NOT NULL,status TEXT NOT NULL DEFAULT 'Open' CHECK(status IN ('Open','Investigating','Resolved')),fixed_release TEXT NULL REFERENCES releases(id));
CREATE INDEX reports_version_category ON reports(version,category);
CREATE TABLE audit(id TEXT PRIMARY KEY,actor TEXT NOT NULL,action TEXT NOT NULL,target TEXT NOT NULL,created TEXT NOT NULL);
CREATE TABLE rate_limits(id TEXT PRIMARY KEY,count INTEGER NOT NULL,expires INTEGER NOT NULL);
