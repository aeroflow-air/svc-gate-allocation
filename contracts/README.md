# Event contract examples

Canonical example messages for the `gate-events` topic, owned by this service
(ADR-0019). One file per event type: `gate-assigned`, `gate-changed`, `gate-released`.

The producer test (`gate-events` contract tests) proves this service's serializer
still produces this shape; consumers pin copies of these files in their own
test suites. Updating a contract deliberately: change the event type and
these examples here first, then copy the changed examples over each
consumer's pinned copies in its repo (consumer-first for a new event type,
per ADR-0019). Never edit a consumer's pin to fix the contract.

Consumers today: the ops dashboard in aeroflow-local.
