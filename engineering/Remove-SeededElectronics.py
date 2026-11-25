"""One-time cleanup for the explicitly requested sample inventory removal."""
import argparse
import re
import sqlite3
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("db", type=Path)
parser.add_argument("--apply", action="store_true")
args = parser.parse_args()
seed = Path(__file__).resolve().parent.parent / "src/SupermarketPos.Infrastructure/Persistence/Migrations/TechCatalog.cs"
groups = re.findall(r'\("([^"]+)", new\[\]\s*\{(.*?)\}\)', seed.read_text(), re.S)
expected = {}
for category, lines in groups:
    for name, _ in re.findall(r'\("([^"]+)", (\d+)\)', lines):
        body = f"040{len(expected) + 1:09}"
        total = sum(int(digit) * (1 if i % 2 == 0 else 3) for i, digit in enumerate(body))
        expected[body + str((-total) % 10)] = (name, category)
assert len(expected) == 100 and len(groups) == 8
db = sqlite3.connect(args.db.resolve().as_uri() + ("?mode=rw" if args.apply else "?mode=ro"), uri=True)
db.execute("pragma foreign_keys=on")
if args.apply:
    db.execute("begin immediate")
try:
    rows = db.execute("""select p.Id,p.Barcode,p.Name,c.Name,p.CategoryId,p.MedicineReferenceId
                         from Products p join Categories c on c.Id=p.CategoryId""").fetchall()
    candidates = [row for row in rows if row[1] in expected and row[5] is None and (row[2], row[3]) == expected[row[1]]]
    uncertain = [row for row in rows if row[1] in expected and row not in candidates]
    blocked = []
    dependencies = {table for (table,) in db.execute("select name from sqlite_master where type='table'")
                    if not table.startswith("sqlite_") and any(fk[2] == "Products" for fk in db.execute('pragma foreign_key_list("' + table.replace('"', '""') + '")'))
                    and table != "InventoryBatches"}
    for row in candidates:
        if any(db.execute('select exists(select 1 from "' + table.replace('"', '""') + '" where ProductId=?)',(row[0],)).fetchone()[0] for table in dependencies):
            blocked.append(row)
        elif db.execute("select exists(select 1 from InventoryBatches where ProductId=?)",(row[0],)).fetchone()[0]:
            blocked.append(row)
    selected = [row for row in candidates if row not in blocked]
    identities = {row[0] for row in selected}
    categories = sorted({row[4] for row in selected if all(r[0] in identities for r in rows if r[4] == row[4])})
    print(f"Matched {len(selected)} sample products and {len(categories)} empty seed categories. Retained {len(uncertain)} changed matches, {len(blocked)} linked products and {len(rows)-len(selected)-len(uncertain)-len(blocked)} other products.")
    if args.apply:
        db.executemany("delete from Products where Id=?",[(row[0],) for row in selected])
        db.executemany("delete from Categories where Id=?",[(category,) for category in categories])
        if db.execute("pragma foreign_key_check").fetchone(): raise RuntimeError("Foreign key check failed")
        db.commit()
except:
    db.rollback()
    raise
finally:
    db.close()
