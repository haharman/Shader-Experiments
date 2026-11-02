# Shader Experiments
<img width="560" height="240" alt="cosmic-interactive2" src="https://github.com/user-attachments/assets/018336f2-17d1-484d-85e9-cd7bfabc0084" />
流体シミュレーション、魚の群れ、星雲、恒星を GPU で描く、インタラクティブな映像作品のプロジェクト（Unity）。
黒板などの平面に触れた手の位置と動きを入力として、画面の中の宇宙が反応する。

- 触れた場所から恒星が生まれ、手に追従し、離すと慣性で飛んで画面中央へ引き寄せられる
- 星の光が、ガスと塵の雲（Nebula）を照らし、陰影と反射光をつくる
- 手の動きが流体の速度場を動かし、雲、パーティクル、文字画像を流す
- 魚の群れは、手が触れると逃げ、逃げる強さに応じて波打ち、青白く光る

シーンは `Assets/CosmicInteractive/CosmicInteractive.unity`。

## 動作環境

| 項目 | 内容 |
|---|---|
| Unity | 6000.3.10f1 |
| レンダーパイプライン | Universal Render Pipeline 17.3.0 |
| 入力 | Input System 1.18.0 |
| GPU | Compute Shader が使えること |
| センサー（任意） | SLAMTEC RPLIDAR C1（シリアル接続） |

センサーがなくても、マウスのポインター操作だけで動かせる。

## 使い方

1. Unity Hub で、このフォルダをプロジェクトとして開く（Unity 6000.3.10f1）
2. `Assets/CosmicInteractive/CosmicInteractive.unity` を開く
3. Play する

画面は、出力解像度（既定は 2560×800）のアスペクト比に合わせて、上下または左右に帯を入れて表示される（`LetterBoxCamera`）。

### 操作

| 操作 | 内容 |
|---|---|
| マウスのボタンを押す | 触れた点として扱う。星が生まれ、魚が逃げ、流体が動く |
| マウスを押したまま動かす | 流体に速度を与える |
| `B` キー | センサーの背景（壁など動かない点）を取り直す |

### センサー（RPLIDAR C1）を使う

1. シーンの `SensorInputSource` と `RpLidarC1` を確認する
2. `RpLidarC1` の `portName`（既定は `COM3`）を、接続した COM ポートに合わせる（`baudRate` は 460800）
3. 何も触れない状態で Play し、背景が記録されるのを待つ
4. 位置を画面に合わせる。手順は [センサー位置の調整](docs/sensor-calibration.md) を参照

`RpLidarC1` の `Failed` からの自動復帰はないので、ケーブルを抜き差ししたときはアプリを再起動する。

## 仕組み

`Bootstrapper` が各コンポーネントを初期化し、毎フレーム次の順に更新する。

```
SensorInputSource ──┐  (センサー / ポインター / Boids の入力)
                    ▼
StableFluids      速度場を計算
TextDye           文字画像を速度場で流す
Particles         パーティクルを速度場で流す
Boids             魚の群れ（速度場に流されつつ、触れた点から逃げる）
Nebula            雲・塵・星空を計算して合成
SolarBodies       触れた点から生まれる星を動かし、Nebula に光源として渡す
```

### 入力

[SensorInputSource](Assets/CosmicInteractive/Scripts/Sensing/SensorInputSource.cs) が、センサーとポインターの入力をまとめる。

- 流体用の入力：触れている点の位置、速度、ID
- 星用の入力：新しく触れた点

センサーの点は、背景差分（`BackgroundModel`）、塊の検出（`BlobDetector`）、追跡（`TouchTracker`）を通して、ポインターと同じ形式にそろえる。

### 流体

[StableFluids](Assets/CosmicInteractive/Scripts/StableFluids.cs) は、Stam の Stable Fluids をコンピュートシェーダーで実装している。外力の追加、移流（Semi-Lagrangian）、拡散、圧力の投影（Jacobi 反復）を行う。速度場は `velocityDownsample` で解像度を下げて計算する。

### Nebula

[Nebula](Assets/CosmicInteractive/Scripts/Nebula.cs) と [Nebula.compute](Assets/CosmicInteractive/Shaders/Nebula.compute) が、星雲の見た目を作る。

- 雲：高さと密度を別々のノイズで生成し、速度場で移流する。観測者に近い面ほど速く流す（視差）
- 照明：星ごとに角度×半径の影の地図（SweepHorizon）を作り、受光量と反射光を求める
- 塵（Veil）：手前の塵による吸収（青から先に削る）と、星の逆光による散乱光
- 合成：星空の上に、雲の層、霧、[O III]、反射光、パーティクルを重ね、魚、塵の順にかける
- 切り替え：雲と塵は、一定時間ごとに別のシードのものへ、ゆっくり寄せる

### 星

[SolarBodies](Assets/CosmicInteractive/Scripts/SolarBodies.cs) が、触れた点ごとに星を出す。

- 星は最大9個をプールして使い回す
- 質量は生成のたびにランダムに決まり、動きの重さ、色温度、光の強さに効く
- [Solar.shader](Assets/CosmicInteractive/Shaders/Solar.shader) が、黒体放射の色、粒状斑、周縁減光、コロナを描く

### 魚

[Boids](Assets/CosmicInteractive/Scripts/Boids.cs) が群れを動かす（最大200匹）。分離・整列・結合に加えて、速度場による流れ、触れた点から離れる動き、表示範囲に戻る動きを足す。[Fish.shader](Assets/CosmicInteractive/Shaders/Fish.shader) は、半透明、縁の Fresnel、体の波打ち、逃げる強さに応じた発光を描く。魚は専用のカメラで別の RenderTexture に描き、Nebula が雲と塵の間に重ねる。

## ディレクトリ構成

```
Assets/
├─ CosmicInteractive/
│   ├─ CosmicInteractive.unity   メインのシーン
│   ├─ Scripts/                  Bootstrapper、StableFluids、Nebula、Boids、SolarBodies など
│   │   └─ Sensing/              RPLIDAR C1 の通信、背景差分、塊の検出、追跡
│   ├─ Shaders/                  コンピュートシェーダーと、魚・恒星のシェーダー
│   ├─ Materials/  Models/  Textures/  Animations/  Settings/
├─ Instances/                    Boid、SolarBody のプレハブ
├─ Settings/                     URP の設定
docs/
└─ sensor-calibration.md         センサー位置の調整手順
```

## ドキュメント

- [センサー位置の調整](docs/sensor-calibration.md)

## ビルド

Build Settings では `CosmicInteractive` シーンだけを有効にしている。ビルドする場合は `File > Build Profiles` から行う。
