# Clinical Visit Ordinal

`Appointment.ClinicalVisitOrdinal` is a one-based, patient-lifetime sequence reserved by the server for eligible clinical appointments. It is intentionally separate from `VisitCount`, which remains the number of attended visits through an appointment date.

## Invariants

- The ordinal is assigned on appointment creation or by the deterministic migration backfill.
- Once assigned, it cannot be changed or reused.
- Rescheduling and appointment-type changes preserve it.
- A numbered appointment cannot be reassigned to another patient; create a new appointment instead.
- Cancellation and no-show preserve the stored reservation but the scheduling API continues to return a null displayed `VisitNumber` for those terminal states.
- Backdated appointments receive the next available ordinal; existing appointments are not renumbered.
- Clients may read the ordinal through the `VisitNumber` projection but may not assign or update it through create, update, PATCH, or sync-push contracts.

The current scope is the full patient record. If clinical numbering must reset by episode of care or authorization, add an explicit episode key before changing the uniqueness boundary; do not infer an episode from dates, appointment types, or payer data.

## Migration and compatibility

The SQLite, SQL Server, and PostgreSQL migrations add a nullable column, deterministically backfill eligible appointments by `StartTimeUtc` and appointment ID, and enforce filtered uniqueness per patient. Cancelled and no-show legacy rows remain unnumbered.

During rolling upgrades, API reads fall back to the previous attended-count projection for eligible null rows. The allocator also counts eligible legacy rows so appointments created after an older seeder or client cannot reuse their visible sequence range.

Before removing the fallback, verify that no eligible appointments have a null ordinal in every tenant. Never repair conflicts by renumbering existing non-null rows.
