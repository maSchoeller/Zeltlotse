# Snowcap

Minimales Claude-Code-Template für agentische Softwareentwicklung.

**Nutzung:** Neues Repo aus diesem Template erzeugen (oder Ordner kopieren),
Claude-Code-Session starten, Idee beschreiben — die Pipeline übernimmt:
Stakeholder-Interview → UX- & Architektur-Design → TDD → Smoketest →
Integration. Jeder Lauf dokumentiert sich selbst in `runs/NNN-slug/`.

Drei Prinzipien: brillante UX/UI · Schulden sichtbar und bewusst ·
kleine parallele Branches (git worktrees).

Der Harness verbessert sich über Retros: verallgemeinerbare Learnings fließen
hierher zurück. Hartes Budget: ≤ 300 Zeilen gesamt.
