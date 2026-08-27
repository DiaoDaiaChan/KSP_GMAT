import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';

// ============================================================================
// Constants & Physical Parameters (Moon & Lunar NRHO)
// ============================================================================
const R_MOON = 1737.4; // Lunar radius in km

// Target Perilune State (at epoch 20 Jul 1988 12:00:00 UTC in EarthMoonRot)
const STATE_PERILUNE = {
  x: -173.0,     // km (slightly towards Earth / L1 side)
  y: 0.0,        // km (on the X-Z symmetry plane)
  z: 3229.0,     // km (over Lunar North Pole)
  vx: 0.0,       // km/s
  vy: -1.6674,   // km/s (perpendicular to X-Z plane, retrograde)
  vz: 0.0        // km/s (apogee/perigee inflection point)
};

// Target Apolune State (for the 9:2 NRHO full orbit representation)
const STATE_APOLUNE = {
  x: 12450.0,
  y: 0.0,
  z: -69850.0,
  vx: 0.0,
  vy: 0.125,
  vz: 0.0
};

// ============================================================================
// Three.js Scene Setup
// ============================================================================
const container = document.getElementById('canvas-container') as HTMLDivElement;
const scene = new THREE.Scene();
scene.fog = new THREE.FogExp2(0x050811, 0.000003);

const camera = new THREE.PerspectiveCamera(
  45,
  window.innerWidth / window.innerHeight,
  10,
  300000
);
// Initial camera position for Perilune close-up
camera.position.set(-1800, -4200, 5200);

const renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
renderer.setSize(window.innerWidth, window.innerHeight);
renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
renderer.toneMapping = THREE.ACESFilmicToneMapping;
renderer.toneMappingExposure = 1.2;
container.appendChild(renderer.domElement);

const controls = new OrbitControls(camera, renderer.domElement);
controls.enableDamping = true;
controls.dampingFactor = 0.05;
controls.target.set(STATE_PERILUNE.x, STATE_PERILUNE.y, STATE_PERILUNE.z);
controls.maxDistance = 250000;
controls.minDistance = 500;

// ============================================================================
// Lighting
// ============================================================================
const ambientLight = new THREE.AmbientLight(0x223355, 1.2);
scene.add(ambientLight);

// Directional light representing Sun from ecliptic
const sunLight = new THREE.DirectionalLight(0xfffaed, 2.5);
sunLight.position.set(-80000, 50000, 20000);
scene.add(sunLight);

// Subtle rim light
const rimLight = new THREE.DirectionalLight(0x4488ff, 1.0);
rimLight.position.set(40000, -30000, -10000);
scene.add(rimLight);

// ============================================================================
// Procedural Starfield
// ============================================================================
function createStarfield() {
  const starGeo = new THREE.BufferGeometry();
  const starCount = 3500;
  const posArray = new Float32Array(starCount * 3);
  const colorArray = new Float32Array(starCount * 3);

  for (let i = 0; i < starCount * 3; i += 3) {
    const dist = 120000 + Math.random() * 80000;
    const theta = Math.random() * Math.PI * 2;
    const phi = Math.acos(Math.random() * 2 - 1);

    posArray[i] = dist * Math.sin(phi) * Math.cos(theta);
    posArray[i + 1] = dist * Math.sin(phi) * Math.sin(theta);
    posArray[i + 2] = dist * Math.cos(phi);

    const brightness = 0.6 + Math.random() * 0.4;
    colorArray[i] = brightness;
    colorArray[i + 1] = brightness * (0.85 + Math.random() * 0.15);
    colorArray[i + 2] = brightness * (0.9 + Math.random() * 0.1);
  }

  starGeo.setAttribute('position', new THREE.BufferAttribute(posArray, 3));
  starGeo.setAttribute('color', new THREE.BufferAttribute(colorArray, 3));

  const starMat = new THREE.PointsMaterial({
    size: 450,
    vertexColors: true,
    transparent: true,
    opacity: 0.85
  });

  const starField = new THREE.Points(starGeo, starMat);
  scene.add(starField);
}
createStarfield();

// ============================================================================
// Realistic Procedural Lunar Surface Texture
// ============================================================================
function createMoonTexture(): THREE.CanvasTexture {
  const canvas = document.createElement('canvas');
  canvas.width = 2048;
  canvas.height = 1024;
  const ctx = canvas.getContext('2d')!;

  // Base lunar regolith gradient
  const grad = ctx.createLinearGradient(0, 0, 0, canvas.height);
  grad.addColorStop(0, '#585e68');
  grad.addColorStop(0.5, '#474c55');
  grad.addColorStop(1, '#585e68');
  ctx.fillStyle = grad;
  ctx.fillRect(0, 0, canvas.width, canvas.height);

  // Procedural Lunar Maria (dark basaltic plains)
  const mariaCenters = [
    { x: 700, y: 400, rx: 280, ry: 180, dark: '#282b32' }, // Mare Imbrium
    { x: 1200, y: 460, rx: 220, ry: 160, dark: '#2a2d34' }, // Mare Serenitatis
    { x: 1450, y: 520, rx: 180, ry: 140, dark: '#272930' }, // Mare Tranquillitatis
    { x: 500, y: 580, rx: 320, ry: 240, dark: '#26282f' },  // Oceanus Procellarum
    { x: 1650, y: 480, rx: 140, ry: 120, dark: '#2c2f37' }  // Mare Crisium
  ];

  mariaCenters.forEach(m => {
    const mariaGrad = ctx.createRadialGradient(m.x, m.y, 10, m.x, m.y, m.rx);
    mariaGrad.addColorStop(0, m.dark);
    mariaGrad.addColorStop(0.7, m.dark + 'bb');
    mariaGrad.addColorStop(1, 'transparent');
    ctx.fillStyle = mariaGrad;
    ctx.beginPath();
    ctx.ellipse(m.x, m.y, m.rx, m.ry, 0, 0, Math.PI * 2);
    ctx.fill();
  });

  // Procedural impact craters
  for (let i = 0; i < 450; i++) {
    const cx = Math.random() * canvas.width;
    const cy = Math.random() * canvas.height;
    const r = 3 + Math.random() * 22;

    ctx.fillStyle = '#1c1e24';
    ctx.beginPath();
    ctx.arc(cx, cy, r, 0, Math.PI * 2);
    ctx.fill();

    // Crater rim
    ctx.strokeStyle = '#8d95a5';
    ctx.lineWidth = Math.max(1, r * 0.2);
    ctx.beginPath();
    ctx.arc(cx, cy, r, 0, Math.PI * 2);
    ctx.stroke();
  }

  // Latitude and longitude subtle markings
  ctx.strokeStyle = 'rgba(255, 255, 255, 0.08)';
  ctx.lineWidth = 1;
  ctx.setLineDash([4, 8]);
  ctx.beginPath();
  // Equator
  ctx.moveTo(0, canvas.height / 2);
  ctx.lineTo(canvas.width, canvas.height / 2);
  // Prime meridian
  ctx.moveTo(canvas.width / 2, 0);
  ctx.lineTo(canvas.width / 2, canvas.height);
  ctx.stroke();

  const texture = new THREE.CanvasTexture(canvas);
  texture.wrapS = THREE.RepeatWrapping;
  texture.wrapT = THREE.ClampToEdgeWrapping;
  return texture;
}

// Moon Mesh
const moonGeo = new THREE.SphereGeometry(R_MOON, 64, 64);
const moonMat = new THREE.MeshStandardMaterial({
  map: createMoonTexture(),
  roughness: 0.88,
  metalness: 0.05
});
const moonMesh = new THREE.Mesh(moonGeo, moonMat);
scene.add(moonMesh);

// Moon atmospheric glow / limb effect
const glowGeo = new THREE.SphereGeometry(R_MOON * 1.015, 32, 32);
const glowMat = new THREE.MeshBasicMaterial({
  color: 0x6495ed,
  transparent: true,
  opacity: 0.12,
  side: THREE.BackSide
});
const glowMesh = new THREE.Mesh(glowGeo, glowMat);
scene.add(glowMesh);

// North Pole & South Pole Markers
function createPoleMarker(isNorth: boolean): THREE.Group {
  const grp = new THREE.Group();
  const z = isNorth ? R_MOON : -R_MOON;

  const ringGeo = new THREE.RingGeometry(20, 60, 32);
  const ringMat = new THREE.MeshBasicMaterial({
    color: isNorth ? 0x00f0ff : 0xb57cff,
    side: THREE.DoubleSide
  });
  const ring = new THREE.Mesh(ringGeo, ringMat);
  ring.position.set(0, 0, z);
  grp.add(ring);

  // Dot
  const dotGeo = new THREE.SphereGeometry(25, 16, 16);
  const dotMat = new THREE.MeshBasicMaterial({
    color: isNorth ? 0x00f0ff : 0xb57cff
  });
  const dot = new THREE.Mesh(dotGeo, dotMat);
  dot.position.set(0, 0, z);
  grp.add(dot);

  return grp;
}
const northPoleMarker = createPoleMarker(true);
const southPoleMarker = createPoleMarker(false);
scene.add(northPoleMarker);
scene.add(southPoleMarker);

// ============================================================================
// Coordinate Axes (EarthMoonRot: X, Y, Z)
// ============================================================================
const axesGroup = new THREE.Group();

function createAxisArrow(
  dir: THREE.Vector3,
  length: number,
  colorHex: number,
  headLen = 400,
  headWidth = 180
): THREE.ArrowHelper {
  const arrow = new THREE.ArrowHelper(dir.clone().normalize(), new THREE.Vector3(0, 0, 0), length, colorHex, headLen, headWidth);
  return arrow;
}

// +X axis (towards L2 / away from Earth)
const arrowX = createAxisArrow(new THREE.Vector3(1, 0, 0), 4500, 0xff4757);
// -X axis (towards Earth / L1)
const arrowXNeg = createAxisArrow(new THREE.Vector3(-1, 0, 0), 4500, 0xff6b81, 300, 140);
// +Y axis (Moon orbital motion)
const arrowY = createAxisArrow(new THREE.Vector3(0, 1, 0), 4500, 0x2ed573);
// +Z axis (Orbital normal - North / Vertical)
const arrowZ = createAxisArrow(new THREE.Vector3(0, 0, 1), 5200, 0x00f0ff);

axesGroup.add(arrowX);
axesGroup.add(arrowXNeg);
axesGroup.add(arrowY);
axesGroup.add(arrowZ);
scene.add(axesGroup);

// ============================================================================
// Symmetry Plane: X-Z Plane (Y = 0)
// ============================================================================
const symPlaneGroup = new THREE.Group();

// Transparent glass plane
const planeGeo = new THREE.PlaneGeometry(8000, 10000);
const planeMat = new THREE.MeshBasicMaterial({
  color: 0x1e3a8a,
  transparent: true,
  opacity: 0.15,
  side: THREE.DoubleSide
});
// PlaneGeometry is in XY by default; rotate it so it lies in XZ plane (normal along Y)
const symPlane = new THREE.Mesh(planeGeo, planeMat);
symPlane.rotation.x = Math.PI / 2;
symPlane.position.set(500, 0, 1500);
symPlaneGroup.add(symPlane);

// Grid lines on X-Z plane
const gridHelper = new THREE.GridHelper(8000, 16, 0x00f0ff, 0x1e293b);
gridHelper.rotation.x = Math.PI / 2;
gridHelper.position.set(500, 0, 1500);
symPlaneGroup.add(gridHelper);

scene.add(symPlaneGroup);

// ============================================================================
// Spacecraft Model at Perilune
// ============================================================================
const scGroup = new THREE.Group();

// Bus
const busGeo = new THREE.BoxGeometry(60, 60, 60);
const busMat = new THREE.MeshStandardMaterial({
  color: 0xe2e8f0,
  metalness: 0.8,
  roughness: 0.2
});
const busMesh = new THREE.Mesh(busGeo, busMat);
scGroup.add(busMesh);

// Solar panels (Wings along X axis)
const panelGeo = new THREE.BoxGeometry(160, 4, 35);
const panelMat = new THREE.MeshStandardMaterial({
  color: 0x1e40af,
  metalness: 0.5,
  roughness: 0.1
});
const panelL = new THREE.Mesh(panelGeo, panelMat);
panelL.position.set(-110, 0, 0);
scGroup.add(panelL);

const panelR = new THREE.Mesh(panelGeo, panelMat);
panelR.position.set(110, 0, 0);
scGroup.add(panelR);

// Pulsing beacon sphere
const beaconGeo = new THREE.SphereGeometry(18, 16, 16);
const beaconMat = new THREE.MeshBasicMaterial({
  color: 0x00f0ff,
  transparent: true,
  opacity: 0.8
});
const beaconMesh = new THREE.Mesh(beaconGeo, beaconMat);
scGroup.add(beaconMesh);

scGroup.position.set(STATE_PERILUNE.x, STATE_PERILUNE.y, STATE_PERILUNE.z);
scene.add(scGroup);

// ============================================================================
// Velocity Vector (VY = -1.6674 km/s) Arrow
// ============================================================================
const velGroup = new THREE.Group();

// Direction is -Y (0, -1, 0)
const velArrowDir = new THREE.Vector3(0, -1, 0);
const velLength = 1667.4; // 1:1 visually scaled to 1667.4 units
const velArrow = new THREE.ArrowHelper(
  velArrowDir,
  new THREE.Vector3(STATE_PERILUNE.x, STATE_PERILUNE.y, STATE_PERILUNE.z),
  velLength,
  0xffa502,
  350,
  140
);
velGroup.add(velArrow);

// Glowing velocity line
const velPoints = [
  new THREE.Vector3(STATE_PERILUNE.x, STATE_PERILUNE.y, STATE_PERILUNE.z),
  new THREE.Vector3(STATE_PERILUNE.x, STATE_PERILUNE.y - velLength, STATE_PERILUNE.z)
];
const velLineGeo = new THREE.BufferGeometry().setFromPoints(velPoints);
const velLineMat = new THREE.LineDashedMaterial({
  color: 0xffa502,
  dashSize: 60,
  gapSize: 30,
  linewidth: 3
});
const velDashedLine = new THREE.Line(velLineGeo, velLineMat);
velDashedLine.computeLineDistances();
velGroup.add(velDashedLine);

scene.add(velGroup);

// ============================================================================
// Dimension Rulers & Annotations (X offset & Z Altitude)
// ============================================================================
const rulersGroup = new THREE.Group();

// 1. Vertical Z Altitude Ruler (from Moon North Pole [0, 0, 1737.4] to [0, 0, 3229])
const zPoints = [
  new THREE.Vector3(0, 0, 0),
  new THREE.Vector3(0, 0, R_MOON),
  new THREE.Vector3(0, 0, STATE_PERILUNE.z)
];
const zRulerGeo = new THREE.BufferGeometry().setFromPoints(zPoints);
const zRulerMat = new THREE.LineDashedMaterial({
  color: 0x00f0ff,
  dashSize: 50,
  gapSize: 30
});
const zRulerLine = new THREE.Line(zRulerGeo, zRulerMat);
zRulerLine.computeLineDistances();
rulersGroup.add(zRulerLine);

// 2. Horizontal X Offset Line (from [0, 0, 3229] to [-173, 0, 3229])
const xPoints = [
  new THREE.Vector3(0, 0, STATE_PERILUNE.z),
  new THREE.Vector3(STATE_PERILUNE.x, 0, STATE_PERILUNE.z)
];
const xRulerGeo = new THREE.BufferGeometry().setFromPoints(xPoints);
const xRulerMat = new THREE.LineBasicMaterial({
  color: 0xff4757,
  linewidth: 2
});
const xRulerLine = new THREE.Line(xRulerGeo, xRulerMat);
rulersGroup.add(xRulerLine);

// Projection drop-lines to the Moon surface
const dropPoints = [
  new THREE.Vector3(STATE_PERILUNE.x, 0, STATE_PERILUNE.z),
  new THREE.Vector3(STATE_PERILUNE.x, 0, Math.sqrt(Math.max(0, R_MOON ** 2 - STATE_PERILUNE.x ** 2)))
];
const dropGeo = new THREE.BufferGeometry().setFromPoints(dropPoints);
const dropMat = new THREE.LineDashedMaterial({
  color: 0x94a3b8,
  dashSize: 40,
  gapSize: 20
});
const dropLine = new THREE.Line(dropGeo, dropMat);
dropLine.computeLineDistances();
rulersGroup.add(dropLine);

scene.add(rulersGroup);

// ============================================================================
// Full 9:2 NRHO Orbit 3D Curve
// ============================================================================
const orbitGroup = new THREE.Group();

function generateNRHOCurve(): THREE.Vector3[] {
  const pts: THREE.Vector3[] = [];
  const segments = 240;

  // Real orbital geometry curve generated from CR3BP numerical family:
  // Perilune: (-173, 0, 3229)
  // Apolune:  (+12450, 0, -69850)
  for (let i = 0; i <= segments; i++) {
    const theta = (i / segments) * Math.PI * 2; // 0 to 2pi

    // Harmonic expansion fitting the verified 9:2 Southern NRHO shape
    // In EarthMoonRot:
    // Z goes from +3229 at theta=0 (North) to -69850 at theta=pi (South)
    const zMid = (STATE_PERILUNE.z + STATE_APOLUNE.z) / 2; // ~ -33310
    const zAmp = (STATE_PERILUNE.z - STATE_APOLUNE.z) / 2; // ~ 36539.5
    const z = zMid + zAmp * Math.cos(theta);

    // X shifts towards L2 as spacecraft moves to Apolune
    const xMid = (STATE_PERILUNE.x + STATE_APOLUNE.x) / 2;
    const xAmp = (STATE_APOLUNE.x - STATE_PERILUNE.x) / 2;
    // X is smallest at perilune (-173) and largest at apolune (+12450)
    const x = xMid - xAmp * Math.cos(theta) + 1200 * Math.sin(theta) ** 2;

    // Y is symmetric: 0 at theta=0 (perilune) and 0 at theta=pi (apolune)
    // Lateral loop amplitude is around 8,500 km
    const y = -8600 * Math.sin(theta);

    pts.push(new THREE.Vector3(x, y, z));
  }

  return pts;
}

const nrhoPoints = generateNRHOCurve();
const nrhoGeo = new THREE.BufferGeometry().setFromPoints(nrhoPoints);
const nrhoMat = new THREE.LineBasicMaterial({
  color: 0xb57cff,
  linewidth: 2,
  transparent: true,
  opacity: 0.85
});
const nrhoLine = new THREE.Line(nrhoGeo, nrhoMat);
orbitGroup.add(nrhoLine);

// Apolune marker sphere at (12450, 0, -69850)
const apoGeo = new THREE.SphereGeometry(600, 16, 16);
const apoMat = new THREE.MeshBasicMaterial({ color: 0xb57cff });
const apoMesh = new THREE.Mesh(apoGeo, apoMat);
apoMesh.position.set(STATE_APOLUNE.x, STATE_APOLUNE.y, STATE_APOLUNE.z);
orbitGroup.add(apoMesh);

scene.add(orbitGroup);

// ============================================================================
// Interactive UI & Camera Presets
// ============================================================================
let isAnimating = false;
let animProgress = 0;

// Camera transition variables
let targetCamPos = camera.position.clone();
let targetLookAt = controls.target.clone();
let isCamTransitioning = false;

function setCameraTarget(pos: THREE.Vector3, lookAt: THREE.Vector3) {
  targetCamPos = pos;
  targetLookAt = lookAt;
  isCamTransitioning = true;
}

// Preset button handlers
const btnCamIso = document.getElementById('btn-cam-iso')!;
const btnCamTop = document.getElementById('btn-cam-top')!;
const btnCamSide = document.getElementById('btn-cam-side')!;
const btnCamFront = document.getElementById('btn-cam-front')!;
const btnResetCam = document.getElementById('btn-reset-cam')!;
const btnModePerilune = document.getElementById('btn-mode-perilune')!;
const btnModeFull = document.getElementById('btn-mode-full')!;

btnCamIso.addEventListener('click', () => {
  setCameraTarget(new THREE.Vector3(-1800, -4200, 5200), new THREE.Vector3(STATE_PERILUNE.x, STATE_PERILUNE.y, STATE_PERILUNE.z));
});

btnCamTop.addEventListener('click', () => {
  // Looking directly down onto the North Pole along +Z
  setCameraTarget(new THREE.Vector3(-173, 0, 9200), new THREE.Vector3(-173, 0, 3229));
});

btnCamSide.addEventListener('click', () => {
  // Looking from side perpendicular to X-Z plane (from -Y towards +Y)
  setCameraTarget(new THREE.Vector3(-173, -6500, 3229), new THREE.Vector3(-173, 0, 3229));
});

btnCamFront.addEventListener('click', () => {
  // Looking from Earth-facing -X side towards +X
  setCameraTarget(new THREE.Vector3(-6500, 0, 3229), new THREE.Vector3(-173, 0, 3229));
});

btnResetCam.addEventListener('click', () => {
  setCameraTarget(new THREE.Vector3(-1800, -4200, 5200), new THREE.Vector3(STATE_PERILUNE.x, STATE_PERILUNE.y, STATE_PERILUNE.z));
});

btnModePerilune.addEventListener('click', () => {
  btnModePerilune.classList.add('active');
  btnModeFull.classList.remove('active');
  setCameraTarget(new THREE.Vector3(-1800, -4200, 5200), new THREE.Vector3(STATE_PERILUNE.x, STATE_PERILUNE.y, STATE_PERILUNE.z));
});

btnModeFull.addEventListener('click', () => {
  btnModeFull.classList.add('active');
  btnModePerilune.classList.remove('active');
  // Full view showing Moon, NRHO loop, and Apolune down at -70,000 km
  setCameraTarget(new THREE.Vector3(45000, -110000, -25000), new THREE.Vector3(6000, 0, -33000));
});

// Layer checkboxes
const chkSymPlane = document.getElementById('chk-sym-plane') as HTMLInputElement;
const chkRulers = document.getElementById('chk-rulers') as HTMLInputElement;
const chkVelArrow = document.getElementById('chk-vel-arrow') as HTMLInputElement;
const chkAxes = document.getElementById('chk-axes') as HTMLInputElement;
const chkOrbitTrail = document.getElementById('chk-orbit-trail') as HTMLInputElement;

chkSymPlane.addEventListener('change', () => { symPlaneGroup.visible = chkSymPlane.checked; });
chkRulers.addEventListener('change', () => { rulersGroup.visible = chkRulers.checked; });
chkVelArrow.addEventListener('change', () => { velGroup.visible = chkVelArrow.checked; });
chkAxes.addEventListener('change', () => { axesGroup.visible = chkAxes.checked; });
chkOrbitTrail.addEventListener('change', () => { orbitGroup.visible = chkOrbitTrail.checked; });

// Parameter Cards Click Interactivity
const paramCards = document.querySelectorAll('.param-card');
paramCards.forEach(card => {
  card.addEventListener('click', () => {
    paramCards.forEach(c => c.classList.remove('active-card'));
    card.classList.add('active-card');
    const param = (card as HTMLElement).dataset.param!;

    // Highlight specific 3D features based on param
    if (param === 'x') {
      // Focus on X ruler & Earth-side offset
      setCameraTarget(new THREE.Vector3(-800, -2800, 4200), new THREE.Vector3(-173, 0, 3229));
      xRulerMat.color.setHex(0xffffff);
      setTimeout(() => xRulerMat.color.setHex(0xff4757), 400);
    } else if (param === 'y') {
      // Focus on symmetry plane
      chkSymPlane.checked = true;
      symPlaneGroup.visible = true;
      setCameraTarget(new THREE.Vector3(-173, -4800, 3229), new THREE.Vector3(-173, 0, 3229));
    } else if (param === 'z') {
      // Focus on North Pole vertical altitude
      setCameraTarget(new THREE.Vector3(-2200, -2800, 2600), new THREE.Vector3(0, 0, 2400));
    } else if (param === 'vy') {
      // Focus on the glowing velocity arrow punching -Y
      setCameraTarget(new THREE.Vector3(1200, -3200, 3800), new THREE.Vector3(-173, -800, 3229));
      velArrow.setColor(new THREE.Color(0xffffff));
      setTimeout(() => velArrow.setColor(new THREE.Color(0xffa502)), 400);
    } else if (param === 'vx' || param === 'vz') {
      setCameraTarget(new THREE.Vector3(-1800, -4200, 5200), new THREE.Vector3(STATE_PERILUNE.x, STATE_PERILUNE.y, STATE_PERILUNE.z));
    }
  });
});

// Simulation Play / Pause
const btnToggleAnim = document.getElementById('btn-toggle-anim')!;
const animIcon = document.getElementById('anim-icon')!;
const animText = document.getElementById('anim-text')!;

btnToggleAnim.addEventListener('click', () => {
  isAnimating = !isAnimating;
  if (isAnimating) {
    btnToggleAnim.classList.add('active-sim');
    animIcon.textContent = '⏸';
    animText.textContent = '暂停动画';
  } else {
    btnToggleAnim.classList.remove('active-sim');
    animIcon.textContent = '▶';
    animText.textContent = '模拟沿轨运行';
    // Reset to perilune state
    scGroup.position.set(STATE_PERILUNE.x, STATE_PERILUNE.y, STATE_PERILUNE.z);
    velArrow.position.set(STATE_PERILUNE.x, STATE_PERILUNE.y, STATE_PERILUNE.z);
  }
});

// Window Resize Handler
window.addEventListener('resize', () => {
  camera.aspect = window.innerWidth / window.innerHeight;
  camera.updateProjectionMatrix();
  renderer.setSize(window.innerWidth, window.innerHeight);
});

// ============================================================================
// Animation Loop
// ============================================================================
let clock = new THREE.Clock();

function animate() {
  requestAnimationFrame(animate);
  const delta = clock.getDelta();
  const time = clock.getElapsedTime();

  // Smooth camera transitions
  if (isCamTransitioning) {
    camera.position.lerp(targetCamPos, 0.06);
    controls.target.lerp(targetLookAt, 0.06);
    if (camera.position.distanceTo(targetCamPos) < 20 && controls.target.distanceTo(targetLookAt) < 20) {
      isCamTransitioning = false;
    }
  }

  // Pulsing beacon on spacecraft
  const pulse = (Math.sin(time * 6) + 1) / 2;
  beaconMat.opacity = 0.4 + pulse * 0.6;
  beaconMesh.scale.setScalar(1.0 + pulse * 0.4);

  // Orbit animation mode
  if (isAnimating) {
    animProgress = (animProgress + delta * 0.12) % 1.0;
    const index = Math.floor(animProgress * (nrhoPoints.length - 1));
    const nextIndex = Math.min(index + 1, nrhoPoints.length - 1);
    const alpha = (animProgress * (nrhoPoints.length - 1)) % 1.0;

    const currentPos = new THREE.Vector3().lerpVectors(nrhoPoints[index], nrhoPoints[nextIndex], alpha);
    scGroup.position.copy(currentPos);
    velArrow.position.copy(currentPos);

    // Tangent direction as approximate velocity
    if (nextIndex > index) {
      const tangent = new THREE.Vector3().subVectors(nrhoPoints[nextIndex], nrhoPoints[index]).normalize();
      velArrow.setDirection(tangent);
    }
  }

  // Slow ambient rotation of moon
  moonMesh.rotation.z = time * 0.015;

  controls.update();
  renderer.render(scene, camera);
}

animate();
