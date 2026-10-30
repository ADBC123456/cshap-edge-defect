# HALCON 过程接口约定

上位机通过 HDevEngine 加载 `halcon/DiscDefect.hdev`，按**过程名**调用其中的本地过程。只要名字和参数（名称、顺序、类型）与下表一致，上位机就能直接接上。算法本身的原理见 `docs/halcon算法/算法讲解.md`。

## 1. 过程签名

写法：`过程名 (输入图形 : 输出图形 : 输入控制 : 输出控制)`

| 过程 | 签名 |
|---|---|
| `dd_default_params` | `( : : : Params)` |
| `dd_locate_disk` | `(Image : Disk : Params : Found, Row, Column, Radius)` |
| `dd_segment` | `(Image, Disk : RawRegion, Candidates : Params, Radius : )` |
| `dd_detect` | `(Image : Disk, RawRegion, Candidates, Defects : Params : Found, Row, Column, Radius, Category, Result)` |

上位机只认这 4 个名字。.hdev 里的其他过程（`dd_to_gray`、`dd_make_roi`、`dd_region_features`、`dd_classify_region`，以及 main 用的 `augment_image`、`write_feature_rows`、`draw_result`）是内部辅助过程。

## 2. 参数含义

| 参数 | 类型 | 说明 |
|---|---|---|
| `Image` | 图像 | 相机或文件原图，可能是 1、3 或 4 通道。过程内部用 `count_channels` 判断后转成灰度 |
| `Params` | dict 句柄 | 全部可调参数。键名用英文小写加下划线，例如 `dyn_threshold_offset` |
| `Disk` | 区域 | 圆盘区域（`gen_circle` 得到）。没找到圆时输出空区域 |
| `Found` | 整数 | 1 表示找到圆，0 表示没找到 |
| `Row`、`Column`、`Radius` | 实数 | 圆心行、列和半径，单位像素。没找到时都输出 0 |
| `RawRegion` | 区域 | `dyn_threshold` 等分割后得到的原始缺陷区域（一个区域，未做连通域分解） |
| `Candidates` | 区域元组 | 经过连通域分解和面积筛选后的候选区域，每个候选是一个区域 |
| `Defects` | 区域元组 | 判定为缺陷的区域，顺序与 `Result` 各列一一对应 |
| `Category` | 字符串 | 整张图的最终类别：`'Q'`、`'H'`、`'K'`、`'W'`、`'NONE'` 之一。上位机转为类别码：Q→1、H→2、K→3、W→4，NONE 及其他→5（NG） |
| `Result` | dict 句柄 | 每个缺陷的特征，**按列存放**，见第 3 节 |

## 3. Result 字典格式

每个键对应一个元组，元组长度等于 `Defects` 中的区域个数，第 i 个元素描述第 i 个缺陷。上位机会把每个键显示成特征表的一列。

| 键 | 是否必需 | 说明 |
|---|---|---|
| `type` | 必需 | 每个缺陷的类别：`'Q'`、`'H'`、`'K'`、`'W'` |
| `area` | 建议 | 面积（像素） |
| 其余键 | 可选 | 想显示什么特征就加什么 |

当前 `DiscDefect.hdev` 输出的键（上位机特征表显示为中文列名）：

| 键 | 中文列名 | 说明 |
|---|---|---|
| `type` | 类别 | Q / H / K / W |
| `is_main` | 主缺陷 | 1 = 决定整张图类别的那个缺陷 |
| `area` | 面积 | 像素 |
| `contrast` | 对比度 | 周围背景平均灰度 − 区域平均灰度 |
| `edge_gap` | 离边距离 | 像素 |
| `pocket_area` | 切角面积 | 像素，Q 判定用 |
| `hole_ratio` | 孔比例 | K 判定用 |
| `thickness` | 厚度 | 最大内切圆半径，W 判定用 |
| `length` | 长度 | 骨架像素数，H 判定用 |
| `score` | 得分 | 面积 × 对比度 |

例子（两个缺陷）：

```
create_dict (Result)
set_dict_tuple (Result, 'type', ['H', 'W'])
set_dict_tuple (Result, 'area', [417, 1530])
```

## 4. 上位机怎么调用（渐进接入）

1. 加载程序，取得本地过程列表。
2. 有 `dd_default_params` 就调它，得到默认参数；再用 `halcon/detector.json` 里的值覆盖同名键。
3. 有 `dd_detect` 就**只调它**，所有图层和类别都从它取得。
4. 没有 `dd_detect` 时：有 `dd_locate_disk` 就调它，显示圆；再有 `dd_segment` 就接着调，显示分割和候选。此时类别显示"算法未完成"。

所以换成一个只写了部分过程的 .hdev 时，上位机也能显示到它能做到的那一步：

| .hdev 里有 | 视觉调试页能看到 |
|---|---|
| 没有 .hdev | 原图 |
| `dd_locate_disk` | 原图 + 圆 |
| 再加 `dd_segment` | 原图 + 圆 + 原始分割 + 候选 |
| `dd_detect` | 全部图层 + 类别 + 特征表 |

## 5. 注意事项

- 过程里的 `dev_display`、`dev_set_color` 等 `dev_*` 算子在上位机中会被忽略。你在 HDevelop 里调试时照常用，不影响上位机。
- 过程不要读写固定路径的文件，也不要打开窗口或相机。图像由上位机传入。
- 改完 .hdev 后在 HDevelop 里保存，再点上位机的"重新加载算法"即可生效，不用重新编译上位机。
- 计时由上位机负责（调用前后计时），过程里不用自己计时。
- 测试桩 `tests/DiscSorter.Tests/fixtures/contract_stub.hdev` 是按本约定写的最小示例，只用来测试上位机，不是论文算法。
