# 发布到 GitHub

面向维护者的操作手册。第一次发布照着走一遍即可。

仓库名：**`MkvAudioSwap`**　许可：**MIT**（ffmpeg 单独标 GPL）

---

## 一、准备（一次性）

### 1. 确认身份

```powershell
git config --global user.name
git config --global user.email
```

GitHub 会按这两项记录提交作者。如果邮箱和 GitHub 账号不一致，提交不会算到你名下
（想改：`git config --global user.email "你的GitHub邮箱"`）。

### 2. 确认没有大文件混进来

**这一步每次提交前都值得跑。** 仓库应该只有几百 KB：

```powershell
cd <仓库目录>
git count-objects -vH            # size-pack 应该在 1 MB 以内
git ls-files | Measure-Object    # 应约 52 个文件
```

一次真实输出（52 文件 / 300 KB）：

```
52 个文件
dist/       ✓ 已忽略（148 MB 构建产物）
tools/      ✓ 已忽略（ffmpeg 二进制）
测试素材/    ✓ 已忽略（53 MB 生成物）
```

`.gitignore` 覆盖了 `dist/`、`bin/`、`obj/`、`tools/`、`测试素材/`。
**发布用的 zip 不进仓库**，它是挂在 Release 上的（见第三节）。

---

## 二、第一次发布：把代码推上去

### 1. 在 GitHub 上建空仓库

打开 <https://github.com/new>：

| 字段 | 填什么 |
|:--|:--|
| Repository name | `MkvAudioSwap` |
| Description | `把视频画面原样保留、换成你自己的音频，全程不重编码。给翻唱作者用的 Windows 小工具。` |
| 可见性 | **Public**（要给大家用） |
| Add a README file | **不勾** |
| Add .gitignore | **不勾** |
| Choose a license | **不勾** |

⚠️ **三个都不要勾**。本地已经有 README、.gitignore、LICENSE 了，
在网页上再建一份会和后续 push 冲突（报 `rejected - fetch first`）。

### 2. 连远程并推送

在建好的仓库页面复制 URL，然后：

```powershell
cd <仓库目录>
git remote add origin https://github.com/<你的用户名>/MkvAudioSwap.git
git push -u origin main
```

第一次推送会弹窗要求登录 GitHub。**登录时建议用浏览器方式**（Git Credential Manager
会自动处理），不要用账号密码 —— GitHub 早已不支持密码推送。

如果弹的是要求输入 token：去 <https://github.com/settings/tokens> 建一个
**Personal Access Token (classic)**，勾 `repo` 权限，把它当密码粘贴

### 3. 填仓库信息（建议）

推送完在仓库页面点右上 **⚙ Settings** → 上方的 **About** 齿轮：

| 项 | 建议值 |
|:--|:--|
| Description | 同上那句 |
| Website | 留空 |
| Topics | `ffmpeg` `mkv` `audio` `video` `windows` `avalonia` `karaoke` `cover` `lossless` `pcm` |

Topics 会显著影响别人能不能搜到这个工具。

---

## 三、发 Release（这一步最关键）

**对不懂技术的人来说，"Releases 里下载 zip"才是真正的入口。**
没有 Release，他们得自己装 .NET 9 SDK 再编译 —— 那不现实。

README 顶部的下载链接指向 `../../releases/latest`，所以**必须有 Release 才不失效**。

### 1. 本地打包

```powershell
cd <仓库目录>
build.bat
```

产物 `dist\替音工具.zip`（约 45 MB）。如果想跳过打 zip 之外的步骤，用 `-SkipZip` 反过来。

> 注意：zip 里的目录结构是**根层直接放 exe 和 bin\**，解压就能用。
> 别改成多套一层文件夹 —— 使用说明里明确写了"bin 必须和 exe 同层"。

### 2. 在 GitHub 上建 Release

仓库页面右侧 **Releases** → **Create a new release**：

| 字段 | 填什么 |
|:--|:--|
| Choose a tag | 输入 `v1.0.0` → 点 **Create new tag on publish** |
| Target | `main` |
| Release title | `v1.0.0 — 首个版本` |
| 描述 | 见下方模板 |
| Attach binaries | **把 `dist\替音工具.zip` 拖进去** |

Release 描述模板：

```markdown
## 下载

点下面的 `替音工具.zip` 下载。解压后双击 `替音工具.exe`。

**不需要安装 Python / .NET / ffmpeg** —— 都打包在里面了。

> ⚠️ `bin` 文件夹必须和 exe 在同一层，不要单独删除。

## 这个版本做什么

把 MV 的画面原样保留、换成你自己的音频，**全程不重编码**。
音频在 DAW 里按线性 PCM 导出（CAF / WAV）即可。

视频流和音频流是逐字节拷贝的，所以画质音质零损失、处理速度只受硬盘限制。

## 要求

Windows 10 / 11，64 位。

## 详细说明

见仓库里的 [docs/使用说明.md](../../blob/main/docs/使用说明.md)。
```

### 3. 验证下载链接

发布后打开仓库首页，README 顶部那条 "前往 Releases 页面下载" 应该能直接跳到 Release。
点进去确认 `替音工具.zip` 能下载、解压后能双击运行。

---

## 四、之后的版本更新

改完代码，流程是：

```powershell
# 1. 跑测试，确认没改坏
dotnet test

# 2. 提交
git add -A
git commit -m "修了什么（一句话）"

# 3. 推送
git push

# 4. 重新打包
build.bat

# 5. 去 GitHub 建新 Release（标签 v1.0.1），把新的 zip 拖进去
```

**版本号建议**（语义化版本）：

| 场景 | 怎么加 | 例子 |
|:--|:--|:--|
| 修 bug | 第三位 +1 | `v1.0.0` → `v1.0.1` |
| 加功能、兼容旧版 | 第二位 +1 | `v1.0.1` → `v1.1.0` |
| 不兼容的改动 | 第一位 +1 | `v1.1.0` → `v2.0.0` |

---

## 五、容易踩的坑

### 1. 网页建仓库时勾了 README

push 会报：

```
! [rejected] main -> main (fetch first)
error: failed to push some refs
```

两种解法，**选一种别混用**：

```powershell
# 解A：把远程那份合并进来（保留网页上的 README）
git pull --rebase origin main
git push -u origin main

# 解B：本地这份覆盖远程（丢弃网页上建的那份）
git push -u origin main --force
```

### 2. 不小心 commit 了 dist/ 或 tools/

如果还没 push，撤销最简单：

```powershell
git rm -r --cached dist tools
git commit --amend
```

**一旦 push 了，大文件就永久留在 git 历史里**，后面再删也还在（克隆时要全量下载）。
这种情况要么删仓库重建，要么用 `git filter-repo` 重写历史。
所以**每次提交前跑一次第一节第 2 步的体积检查**。

### 3. `.ps1` 的 BOM 被编辑器吃掉

`build.ps1` 和 `docs\生成测试素材.ps1` 必须保存为 **UTF-8 带 BOM**，
否则 Windows PowerShell 5.1 按 ANSI 读，中文全乱码、报错还指不到原因。

```powershell
[IO.File]::ReadAllBytes('build.ps1')[0..2]   # 应为 EF BB BF
```

有些编辑器（和某些自动化工具）保存时会静默去掉 BOM，改完脚本记得查一下。

### 4. 中文文件名在 git 里显示成乱码

`git status` 里看到 `"docs/\344\275\277\347\224\250\350\257\264\346\230\216.md"`
是正常的 —— git 默认把非 ASCII 路径转义显示。想看中文：

```powershell
git -c core.quotepath=false status
```

也可以在 `.gitconfig` 里永久设置 `[core] quotepath = false`。

### 5. 换行符噪声

`.gitattributes` 已经把规则定死了（源码 LF、`.bat`/`.ps1` CRLF）。
如果看到"整个文件都变了"的 diff，多半是某个工具改了换行符，检查这个文件。

---

## 六、许可与再分发

| 部分 | 许可 |
|:--|:--|
| 本仓库代码 | MIT（见 [LICENSE](../LICENSE)） |
| zip 里的 `bin\ffmpeg.exe` | **GPL v3**（来自 gyan.dev 的 essentials 构建） |

ffmpeg 是独立程序，本工具只通过 `Process.Start` 调用它，没有链接它的代码，
所以两者许可分开、互不影响。

**别人转载你的 zip 时，ffmpeg 的 GPL 义务跟着走**（要一并提供许可说明）。
README 和 LICENSE 里都写了这一点，正常转发不会漏。

如果你以后想换成 LGPL 版的 ffmpeg（不含 libx264 等 GPL 组件），
注意**预览功能依赖 libx264 编码**，换完预览会失效 —— 需要另找编码器或改成不编码。
