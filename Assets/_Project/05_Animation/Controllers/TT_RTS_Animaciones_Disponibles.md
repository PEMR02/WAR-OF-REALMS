# Animaciones disponibles — Toony Tiny People TT_RTS

**Ruta base:** `Assets/ToonyTinyPeople/TT_RTS/TT_RTS_Standard/animation/`

Cada clip suele seguir el patrón: **idle**, **walk** / **walk_rm**, **run** / **run_rm**, **attack** / **attack_A** / **attack_B**, **damage**, **death_A** / **death_B**. El sufijo `_rm` = root motion.

---

## 1. Infantería (animation_infantry)

### Archer — `animation_infantry/Archer/`
| Clip | Descripción |
|------|-------------|
| archer_01_idle | Idle |
| archer_02_walk / archer_02_walk_rm | Caminar |
| archer_03_run / archer_03_run_rm | Correr |
| archer_04_attack_A / archer_04_attack_B | Disparo |
| archer_05_damage | Recibir daño |
| archer_06_death_A / archer_06_death_B | Muerte |

### Infantry (espada/escudo, milicia) — `animation_infantry/Infantry/`
| Clip | Descripción |
|------|-------------|
| infantry_01_idle | Idle |
| infantry_02_walk / infantry_02_walk_rm | Caminar |
| infantry_03_run / infantry_03_run_rm | Correr |
| infantry_04_attack_A / infantry_04_attack_B | Ataque |
| infantry_05_damage | Daño |
| infantry_06_death_A / infantry_06_death_B | Muerte |
| infantry_07_punch_A / infantry_07_punch_B | Puñetazo (variante) |

### Shield (escudo) — `animation_infantry/Shield/`
| Clip | Descripción |
|------|-------------|
| shield_01_idle … shield_06_death_B | Idle, walk, run, attack_A/B, damage, death_A/B |

### Spear (lanza) — `animation_infantry/Spear/`
| Clip | Descripción |
|------|-------------|
| spear_01_idle … spear_06_death_B | Idle, walk, run, attack_A/B, damage, death_A/B |

### Polearm (arma de asta) — `animation_infantry/Polearm/`
| Clip | Descripción |
|------|-------------|
| polearm_01_idle … polearm_06_death_B | Idle, walk, run, attack_A/B, damage, death_A/B |

### TwoHanded (espada a dos manos) — `animation_infantry/TwoHanded/`
| Clip | Descripción |
|------|-------------|
| twohanded_01_idle … twohanded_06_death_B | Idle, walk, run, attack_A/B, damage, death_A/B |

### Crossbow (ballesta) — `animation_infantry/Crossbow/`
| Clip | Descripción |
|------|-------------|
| crossbow_01_idle … crossbow_06_death_B | Idle, walk, run, attack, damage, death_A/B |

### Staff (bastón/mago) — `animation_infantry/Staff/`
| Clip | Descripción |
|------|-------------|
| staff_01_idle … staff_06_death_B | Idle, walk, run, attack_A/B, damage, death_A/B |
| staff_07_cast_A / staff_07_cast_B | Hechizo |

---

## 2. Caballería (animation_cavalry)

### Cavalry (caballo básico) — `animation_cavalry/cavalry/`
| Clip | Descripción |
|------|-------------|
| cavalry_01_idle … cavalry_06_death_B | Idle, walk, run, attack, damage, death_A/B |

### Cavalry Archer — `animation_cavalry/cavalry_archer/`
| Clip | Descripción |
|------|-------------|
| cav_archer_01_idle … cav_archer_06_death_B | Idle, walk, run, attack, damage, death_A/B |

### Cavalry Crossbow — `animation_cavalry/cavalry_crossbow/`
| Clip | Descripción |
|------|-------------|
| cav_crossbow_01_idle … cav_crossbow_06_death_B | Idle, walk, run, attack, damage, death_A/B |

### Cavalry Shield — `animation_cavalry/cavalry_shield/`
| Clip | Descripción |
|------|-------------|
| cav_shield_01_idle … cav_shield_06_death_B | Idle, walk, run, attack, damage, death_A/B |

### Cavalry Spear A/B — `animation_cavalry/cavalry_spear_A/` y `cavalry_spear_B/`
| Clip | Descripción |
|------|-------------|
| cav_spear_A_01_idle … cav_spear_A_06_death_B | Idle, walk, run, attack, damage, death_A/B |
| cav_spear_B_* | Misma estructura, variante B |

### Cavalry Staff (mago a caballo) — `animation_cavalry/cavalry_staff/`
| Clip | Descripción |
|------|-------------|
| cav_staff_01_idle … cav_staff_06_death_B | Idle, walk, run, attack, damage, death_A/B |
| cav_staff_07_cast_A / cav_staff_07_cast_B | Hechizo |

---

## 3. Máquinas / Carro (animation_machines)

### Cart (carro / settler) — `animation_machines/Cart/`
| Clip | Descripción |
|------|-------------|
| cart_01_idle | Idle (usado en sample_settler) |
| cart_02_move | Movimiento |
| cart_03_death | Muerte |

### Ram (ariete) — `animation_machines/Ram/`
| Clip | Descripción |
|------|-------------|
| ram_01_idle, ram_02_move, ram_03_attack, ram_04_damage, ram_05_death | Idle, mover, ataque, daño, muerte |

### Catapult — `animation_machines/Catapult/`
| Clip | Descripción |
|------|-------------|
| catapult_01_idle … catapult_05_death | Idle, move, attack, damage, death |

### Ballista — `animation_machines/Ballista/`
| Clip | Descripción |
|------|-------------|
| ballista_01_idle … ballista_05_death | Idle, move, attack, damage, death |

---

## 4. Sample controllers (solo idle)

En `sample_scene/animation_samples/` los controllers de ejemplo usan **solo un estado idle** cada uno (sin parámetro Speed ni Walk/Run). Para movimiento en juego hay que usar los controllers de **`Assets/_Project/05_Animation/Controllers/`**, que sí tienen Idle/Walk/Run y el parámetro **Speed** (ver README_Animators.md).

| Controller TT sample | Clip que usa | Uso típico |
|----------------------|--------------|------------|
| sample_settler | cart_01_idle | Aldeano / carro |
| sample_infantry | infantry_01_idle | Milicia / scout |
| sample_archer | archer_01_idle | Arquero |
| sample_spearman | spear_01_idle | Lancero |
| sample_two_handed | twohanded_01_idle | Espadachín 2 manos |
| sample_polearm | polearm_01_idle | Arma de asta |
| sample_crossbow | crossbow_01_idle | Ballestero |
| sample_caster | staff_01_idle | Mago |
| sample_cavalry | cavalry_01_idle | Caballería |
| sample_cavalry_spear | cav_spear_01_idle_A | Caballería con lanza |
| sample_cavalry_archer | cav_archer_01_idle | Arquero a caballo |
| sample_cavalry_caster | cav_staff_01_idle | Mago a caballo |

---

## Resumen por tipo de unidad (tu proyecto)

| Tu prefab | Rig | Carpeta TT recomendada |
|-----------|-----|-------------------------|
| PF_Aldeano | Infantry | animation_infantry/Infantry o Cart (settler) |
| PF_Scout | Infantry | animation_infantry/Infantry |
| PF_Swordman (Milicia) | Infantry | animation_infantry/Infantry o Shield |
| PF_Lancero | Infantry | animation_infantry/Spear |
| PF_Archer | Infantry | animation_infantry/Archer |
| PF_Mounted_King (Caballero) | Cavalry | animation_cavalry/cavalry |

Los controllers de **`_Project/05_Animation/Controllers/`** referencian estas carpetas y exponen **Speed** para que `UnitAnimatorDriver` sincronice Idle/Walk/Run en runtime.
