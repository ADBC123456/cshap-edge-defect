# 识别结果对比

## opencv_synthetic

- 类别完全正确：10/13 = 76.9%
- 分拣正确（NONE 归 NG）：10/13 = 76.9%
- 找到圆：13/13
- 耗时 ms：平均 30.1，最大 53.0

| 真实 \ 识别 | Q | H | K | W | NONE | 召回率 |
|---|---|---|---|---|---|---|
| Q | 2 | 0 | 0 | 0 | 0 | 100% |
| H | 0 | 0 | 0 | 2 | 1 | 0% |
| K | 0 | 0 | 2 | 0 | 0 | 100% |
| W | 0 | 0 | 0 | 2 | 0 | 100% |
| NONE | 0 | 0 | 0 | 0 | 4 | 100% |

## halcon_synthetic

- 类别完全正确：11/13 = 84.6%
- 分拣正确（NONE 归 NG）：11/13 = 84.6%
- 找到圆：13/13
- 耗时 ms：平均 3.5，最大 5.2

| 真实 \ 识别 | Q | H | K | W | NONE | 召回率 |
|---|---|---|---|---|---|---|
| Q | 1 | 0 | 0 | 1 | 0 | 50% |
| H | 0 | 3 | 0 | 0 | 0 | 100% |
| K | 0 | 0 | 2 | 0 | 0 | 100% |
| W | 0 | 0 | 0 | 2 | 0 | 100% |
| NONE | 0 | 0 | 0 | 1 | 3 | 75% |

## 逐图结果（* 表示识别错误）

| 文件 | 真实 | opencv_synthetic | halcon_synthetic | Δ圆心 px | ΔR px |
|---|---|---|---|---|---|
| H_curved.png | H | *W (W) | H (H) | 0.0 | -0.5 |
| H_curved_with_ghost.png | H | *W (W) | H (H) | 0.0 | -0.5 |
| H_straight.png | H | *NONE | H (H) | 0.0 | -0.5 |
| K_ellipse_ring.png | K | K (K) | K (K) | 0.0 | -0.5 |
| K_ring.png | K | K (K) | K (K) | 0.0 | -0.5 |
| NONE_clean.png | NONE | NONE | NONE | 0.0 | -0.5 |
| NONE_faint_ghost.png | NONE | NONE | NONE | 0.0 | +0.5 |
| NONE_radial_line.png | NONE | NONE | *W (W) | 0.0 | -0.6 |
| NONE_rim_arc.png | NONE | NONE | NONE | 1.4 | -0.4 |
| Q_single_contact_notch.png | Q | Q (Q) | *W (W) | 0.1 | -0.6 |
| Q_two_contact_v.png | Q | Q (Q) | Q (Q) | 0.0 | -0.5 |
| W_blob_small_hole.png | W | W (W) | W (W) | 0.0 | -0.5 |
| W_large_blob.png | W | W (W) | W (W) | 0.0 | -0.5 |
