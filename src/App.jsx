import { useEffect, useMemo, useRef, useState } from "react";
import {
  Aperture,
  ArrowsOutSimple,
  BatteryMedium,
  CaretUp,
  Compass,
  Crop,
  Cube,
  FileText,
  FilmSlate,
  FolderSimple,
  ImageSquare,
  MagnifyingGlass,
  Microphone,
  MusicNotes,
  PaintBrush,
  Play,
  SpeakerHigh,
  SquaresFour,
  Stack,
  TextT,
  Waveform,
  WifiHigh,
  WindowsLogo,
} from "@phosphor-icons/react";

const TASKBAR_HEIGHT = 68;
const EDGE_GAP = 22;
const DEFAULT_WIDTH = 444;
const DEFAULT_HEIGHT = 340;
const DEFAULT_RIGHT_GAP = 116;

const shortcuts = [
  { id: "crop", label: "裁剪", icon: Crop, color: "#9b58dc" },
  { id: "player", label: "播放器", icon: Play, color: "#eea42e" },
  { id: "camera", label: "相机", icon: Aperture, color: "#53aeba" },
  { id: "music", label: "音乐", icon: MusicNotes, color: "#dc607c" },
  { id: "paint", label: "画笔", icon: PaintBrush, color: "#4c9fec" },
  { id: "gallery", label: "图库", icon: ImageSquare, color: "#65ae78" },
  { id: "record", label: "录音", icon: Microphone, color: "#eebc2d" },
  { id: "audio", label: "音频", icon: Waveform, color: "#95b72c" },
  { id: "video", label: "视频", icon: FilmSlate, color: "#ea6a5f" },
  { id: "layers", label: "图层", icon: Stack, color: "#8d55d6" },
  { id: "type", label: "文字", icon: TextT, color: "#40aeba" },
  { id: "model", label: "模型", icon: Cube, color: "#727d8f" },
];

const desktopItems = [
  { id: "project", label: "项目资料", icon: FolderSimple, color: "#f4bc3f" },
  { id: "brief", label: "创作说明", icon: FileText, color: "#f4f6fa" },
];

function initialFrame() {
  const viewportWidth = typeof window === "undefined" ? 1440 : window.innerWidth;
  return {
    x: Math.max(EDGE_GAP, viewportWidth - DEFAULT_WIDTH - DEFAULT_RIGHT_GAP),
    y: 98,
    width: DEFAULT_WIDTH,
    height: DEFAULT_HEIGHT,
  };
}

function clamp(value, min, max) {
  return Math.min(Math.max(value, min), max);
}

export function App() {
  const [frame, setFrame] = useState(initialFrame);
  const [launchFeedback, setLaunchFeedback] = useState({ id: 0, label: "", visible: false });
  const [accessStatus, setAccessStatus] = useState("");
  const [isMoving, setIsMoving] = useState(false);
  const [isResizing, setIsResizing] = useState(false);
  const dragState = useRef(null);
  const resizeState = useRef(null);
  const toastTimer = useRef(null);
  const toastFrame = useRef(null);
  const launchId = useRef(0);

  const columns = useMemo(
    () => clamp(Math.round(Math.sqrt((shortcuts.length * frame.width) / frame.height)), 2, 6),
    [frame.height, frame.width],
  );
  const rows = Math.ceil(shortcuts.length / columns);
  const gap = frame.width >= 420 ? 28 : frame.width >= 340 ? 18 : 10;
  const padding = frame.width < 360 ? 14 : 20;
  const tileSize = clamp(
    Math.min(
      (frame.width - padding * 2 - gap * (columns - 1)) / columns,
      (frame.height - padding * 2 - gap * (rows - 1)) / rows,
    ),
    46,
    82,
  );

  useEffect(() => {
    const keepInView = () => {
      setFrame((current) => {
        const maxWidth = Math.max(260, window.innerWidth - EDGE_GAP * 2);
        const maxHeight = Math.max(
          220,
          window.innerHeight - TASKBAR_HEIGHT - EDGE_GAP * 2,
        );
        const width = Math.min(current.width, maxWidth);
        const height = Math.min(current.height, maxHeight);
        return {
          ...current,
          width,
          height,
          x: clamp(current.x, EDGE_GAP, Math.max(EDGE_GAP, window.innerWidth - width - EDGE_GAP)),
          y: clamp(
            current.y,
            EDGE_GAP,
            Math.max(EDGE_GAP, window.innerHeight - TASKBAR_HEIGHT - height - EDGE_GAP),
          ),
        };
      });
    };

    window.addEventListener("resize", keepInView);
    return () => window.removeEventListener("resize", keepInView);
  }, []);

  useEffect(() => {
    const updatePointerInteraction = (event) => {
      const resize = resizeState.current;
      if (resize && resize.pointerId === event.pointerId) {
        const maxWidth = Math.max(260, window.innerWidth - resize.frame.x - EDGE_GAP);
        const maxHeight = Math.max(
          220,
          window.innerHeight - TASKBAR_HEIGHT - resize.frame.y - EDGE_GAP,
        );
        setFrame((current) => ({
          ...current,
          width: clamp(resize.frame.width + event.clientX - resize.startX, 260, maxWidth),
          height: clamp(resize.frame.height + event.clientY - resize.startY, 220, maxHeight),
        }));
        return;
      }

      const drag = dragState.current;
      if (!drag || drag.pointerId !== event.pointerId) return;
      const maxX = Math.max(EDGE_GAP, window.innerWidth - drag.frame.width - EDGE_GAP);
      const maxY = Math.max(
        EDGE_GAP,
        window.innerHeight - TASKBAR_HEIGHT - drag.frame.height - EDGE_GAP,
      );
      setFrame((current) => ({
        ...current,
        x: clamp(drag.frame.x + event.clientX - drag.startX, EDGE_GAP, maxX),
        y: clamp(drag.frame.y + event.clientY - drag.startY, EDGE_GAP, maxY),
      }));
    };

    const finishPointerInteraction = (event) => {
      if (resizeState.current?.pointerId === event.pointerId) {
        resizeState.current = null;
        setIsResizing(false);
      }
      if (dragState.current?.pointerId === event.pointerId) {
        dragState.current = null;
        setIsMoving(false);
      }
    };

    window.addEventListener("pointermove", updatePointerInteraction);
    window.addEventListener("pointerup", finishPointerInteraction);
    window.addEventListener("pointercancel", finishPointerInteraction);
    return () => {
      window.removeEventListener("pointermove", updatePointerInteraction);
      window.removeEventListener("pointerup", finishPointerInteraction);
      window.removeEventListener("pointercancel", finishPointerInteraction);
    };
  }, []);

  useEffect(
    () => () => {
      if (toastTimer.current) window.clearTimeout(toastTimer.current);
      if (toastFrame.current) window.cancelAnimationFrame(toastFrame.current);
    },
    [],
  );

  const launchShortcut = (label) => {
    launchId.current += 1;
    const id = launchId.current;
    setLaunchFeedback({ id, label, visible: false });
    if (toastTimer.current) window.clearTimeout(toastTimer.current);
    if (toastFrame.current) window.cancelAnimationFrame(toastFrame.current);
    toastFrame.current = window.requestAnimationFrame(() => {
      setLaunchFeedback((current) =>
        current.id === id ? { ...current, visible: true } : current,
      );
    });
    toastTimer.current = window.setTimeout(() => {
      setLaunchFeedback((current) =>
        current.id === id ? { ...current, visible: false } : current,
      );
    }, 1800);
  };

  const beginMove = (event) => {
    if (event.button !== 0 || event.target.closest("button")) return;
    event.currentTarget.setPointerCapture(event.pointerId);
    dragState.current = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      frame,
    };
    setIsMoving(true);
  };

  const beginResize = (event) => {
    if (event.button !== 0 || !event.isPrimary) return;
    event.stopPropagation();
    event.currentTarget.setPointerCapture(event.pointerId);
    resizeState.current = {
      pointerId: event.pointerId,
      startX: event.clientX,
      startY: event.clientY,
      frame,
    };
    setIsResizing(true);
  };

  const moveWithKeyboard = (event) => {
    if (!event.altKey) return;
    const movement = {
      ArrowLeft: [-8, 0],
      ArrowRight: [8, 0],
      ArrowUp: [0, -8],
      ArrowDown: [0, 8],
    }[event.key];
    if (!movement) return;
    event.preventDefault();
    const nextX = clamp(
      frame.x + movement[0],
      EDGE_GAP,
      Math.max(EDGE_GAP, window.innerWidth - frame.width - EDGE_GAP),
    );
    const nextY = clamp(
      frame.y + movement[1],
      EDGE_GAP,
      Math.max(EDGE_GAP, window.innerHeight - TASKBAR_HEIGHT - frame.height - EDGE_GAP),
    );
    setFrame((current) => ({ ...current, x: nextX, y: nextY }));
    setAccessStatus(
      nextX === frame.x && nextY === frame.y
        ? "整理块已到达桌面边界"
        : `整理块位置：横向 ${Math.round(nextX)}，纵向 ${Math.round(nextY)}`,
    );
  };

  const resizeWithKeyboard = (event) => {
    const sizeChange = {
      ArrowLeft: [-16, 0],
      ArrowRight: [16, 0],
      ArrowUp: [0, -16],
      ArrowDown: [0, 16],
    }[event.key];
    if (!sizeChange) return;
    event.preventDefault();
    event.stopPropagation();
    const maxWidth = Math.max(260, window.innerWidth - frame.x - EDGE_GAP);
    const maxHeight = Math.max(
      220,
      window.innerHeight - TASKBAR_HEIGHT - frame.y - EDGE_GAP,
    );
    const nextWidth = clamp(frame.width + sizeChange[0], 260, maxWidth);
    const nextHeight = clamp(frame.height + sizeChange[1], 220, maxHeight);
    setFrame((current) => ({ ...current, width: nextWidth, height: nextHeight }));
    setAccessStatus(
      nextWidth === frame.width && nextHeight === frame.height
        ? "整理块已到达尺寸边界"
        : `整理块尺寸：宽 ${Math.round(nextWidth)}，高 ${Math.round(nextHeight)}`,
    );
  };

  return (
    <main className="desktop" aria-label="桌面整理原型">
      <div className="desktop-items" aria-label="桌面文件">
        {desktopItems.map(({ id, label, icon: Icon, color }) => (
          <button className="desktop-item" type="button" aria-label={label} key={id}>
            <Icon size={54} weight="fill" color={color} />
          </button>
        ))}
      </div>

      <section
        className={`organizer ${isMoving ? "is-moving" : ""} ${isResizing ? "is-resizing" : ""}`}
        aria-label="快捷方式整理块，按住空白处拖动，按 Alt 加方向键移动"
        tabIndex="0"
        onKeyDown={moveWithKeyboard}
        onPointerDown={beginMove}
        style={{
          left: frame.x,
          top: frame.y,
          width: frame.width,
          height: frame.height,
          "--grid-columns": columns,
          "--grid-rows": rows,
          "--grid-gap": `${gap}px`,
          "--grid-padding": `${padding}px`,
          "--tile-size": `${tileSize}px`,
        }}
      >
        <div className="shortcut-grid">
          {shortcuts.map(({ id, label, icon: Icon, color }) => (
            <button
              className="shortcut"
              type="button"
              aria-label={`启动${label}`}
              title={`启动${label}`}
              key={id}
              onClick={() => launchShortcut(label)}
              style={{ "--shortcut-color": color }}
            >
              <Icon size="54%" weight="bold" aria-hidden="true" />
            </button>
          ))}
        </div>

        <button
          className="resize-handle"
          type="button"
          aria-label="调整整理块大小，使用方向键微调"
          title="拖动调整大小"
          onPointerDown={beginResize}
          onKeyDown={resizeWithKeyboard}
        >
          <ArrowsOutSimple size={14} weight="bold" aria-hidden="true" />
        </button>
      </section>

      <div
        key={launchFeedback.id}
        className={`launch-toast ${launchFeedback.visible ? "is-visible" : ""}`}
        role="status"
        aria-live="polite"
      >
        {launchFeedback.label ? `正在启动 · ${launchFeedback.label}` : ""}
      </div>
      <div className="sr-only" role="status" aria-live="polite">{accessStatus}</div>

      <footer className="taskbar" aria-label="Windows 任务栏">
        <div className="taskbar-apps">
          <button type="button" aria-label="开始"><WindowsLogo size={27} weight="fill" /></button>
          <button type="button" aria-label="搜索"><MagnifyingGlass size={28} /></button>
          <button type="button" aria-label="任务视图"><SquaresFour size={27} weight="fill" /></button>
          <button type="button" aria-label="文件"><FolderSimple size={29} weight="fill" color="#e7ad37" /></button>
          <button type="button" aria-label="浏览器"><Compass size={29} weight="duotone" color="#41a6d7" /></button>
        </div>
        <div className="taskbar-status" aria-label="系统状态">
          <CaretUp size={16} />
          <WifiHigh size={20} />
          <SpeakerHigh size={20} />
          <BatteryMedium size={24} />
          <span className="taskbar-time">10:30<br /><span>2026/08/01</span></span>
        </div>
      </footer>
    </main>
  );
}
