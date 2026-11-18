import math
import sys
from dataclasses import dataclass
from typing import Callable, List, Tuple
import pygame


G = 35.00
EARTH_ORBIT_RADIUS = 500.0
EARTH_RADIUS = 6.0
SUN_RADIUS = 66.833

BODY_COLORS: List[Tuple[int, int, int]] = [
    (255, 0, 0),       # Red
    (0, 0, 255),       # Blue
    (0, 255, 0),       # Green
    (237, 130, 237),   # Violet
    (128, 0, 128),     # Purple
    (255, 166, 0),     # Orange
    (255, 69, 0),      # OrangeRed
]


@dataclass
class SolarBody:
    mass: float
    radius: float
    color: Tuple[int, int, int]
    position: pygame.math.Vector2
    velocity: pygame.math.Vector2
    surface: pygame.Surface | None = None


def rotate_vec(v: pygame.math.Vector2, angle_rad: float) -> pygame.math.Vector2:
    cos_a = math.cos(angle_rad)
    sin_a = math.sin(angle_rad)
    return pygame.math.Vector2(
        v.x * cos_a - v.y * sin_a,
        v.x * sin_a + v.y * cos_a,
    )


def clockwise_perpendicular(v: pygame.math.Vector2) -> pygame.math.Vector2:
    return rotate_vec(v, math.pi / 2.0)


def unit_vec(v: pygame.math.Vector2) -> pygame.math.Vector2:
    length = v.length()
    return v / length if length > 0 else pygame.math.Vector2(0, 0)


def two_bodies_force(solar_from: SolarBody, solar_to: SolarBody) -> pygame.math.Vector2:
    r = solar_to.position - solar_from.position
    len_sq = r.length_squared()
    if len_sq == 0:
        return pygame.math.Vector2(0, 0)
    unit_accel = r / math.sqrt(len_sq)
    gravity_force = G * solar_from.mass * solar_to.mass / len_sq
    return gravity_force * unit_accel


def all_bodies_force(bodies: List[SolarBody], from_idx: int) -> pygame.math.Vector2:
    total_force = pygame.math.Vector2(0, 0)
    body_from = bodies[from_idx]
    for to_idx, body_to in enumerate(bodies):
        if from_idx != to_idx:
            total_force += two_bodies_force(body_from, body_to)
    return total_force


def create_circle_surface(radius: int, color: Tuple[int, int, int], draws: int = 1) -> pygame.Surface:
    draws = max(1, draws)
    outer_radius = radius * 2 + 2
    surface = pygame.Surface((outer_radius, outer_radius), pygame.SRCALPHA)
    center_x = outer_radius // 2
    center_y = outer_radius // 2

    r = radius
    rgba = (*color, 255)
    for _ in range(draws):
        if r <= 0:
            break
        angle_step = 1.0 / (3.0 * r)
        angle = 0.0
        two_pi = 2.0 * math.pi
        while angle < two_pi:
            x = int(center_x + r * math.cos(angle))
            y = int(center_y + r * math.sin(angle))
            if 0 <= x < outer_radius and 0 <= y < outer_radius:
                surface.set_at((x, y), rgba)
            angle += angle_step
        r -= 1

    return surface


class Game:
    def __init__(self, width: int = 1920, height: int = 1080) -> None:
        pygame.init()
        self.width = width
        self.height = height
        self.screen = pygame.display.set_mode((self.width, self.height))
        pygame.display.set_caption("Solar System - Pygame")

        self.font = pygame.font.SysFont(["Arial", "Tahoma", "Verdana", "Calibri"], 18)
        self.controls_lines = [
            "Space: restart Left/Right: next/prev",
            "F: toggle forces Esc: exit",
        ]
        self.controls_surfaces = [
            self.font.render(line, True, (255, 255, 255)) for line in self.controls_lines
        ]

        self.bodies: List[SolarBody] = []
        self.show_forces = False
        self.actions: List[Callable[[], None]] = [
            self.simple_orbit,
            self.body_orbit_spiral,
            self.party_over_spiral,
            self.sun_dance,
            self.square_grid,
            self.body_circle,
        ]
        self.action_index = 1
        self.actions[self.action_index]()

    def create_textures(self) -> None:
        for body in self.bodies:
            r = int(body.radius)
            draws = int(0.200 * body.radius)
            body.surface = create_circle_surface(r, body.color, draws)

    def _setup_spiral(
        self,
        count: int,
        initial_speed: float,
        start_orbit_scale: float,
        final_orbit_scale: float,
        angle_range: float,
        body_mass: float,
        sun_velocity: Tuple[float, float] = (0.0, 0.0),
    ) -> None:
        self.bodies = []
        sun_pos = pygame.math.Vector2(self.width / 2.0, self.height / 2.0)
        self.bodies.append(
            SolarBody(
                mass=333054.2531815,
                radius=SUN_RADIUS,
                color=(255, 255, 0),
                position=sun_pos,
                velocity=pygame.math.Vector2(*sun_velocity),
            )
        )

        orbit_scale = final_orbit_scale - start_orbit_scale
        for i in range(1, count):
            unit_step = float(i - 1) / float(count - 1)
            angle = unit_step * angle_range
            scale = start_orbit_scale + unit_step * orbit_scale
            color = BODY_COLORS[i % len(BODY_COLORS)]
            body_unit_x = pygame.math.Vector2(EARTH_ORBIT_RADIUS, 0.0)
            to_body = rotate_vec(body_unit_x, angle)
            pos = sun_pos + scale * to_body
            vel = (1.0 / scale) * initial_speed * unit_vec(clockwise_perpendicular(to_body))
            self.bodies.append(
                SolarBody(
                    mass=body_mass,
                    radius=EARTH_RADIUS,
                    color=color,
                    position=pos,
                    velocity=vel,
                )
            )

        self.create_textures()

    def simple_orbit(self) -> None:
        self._setup_spiral(
            count=3,
            initial_speed=120.0,
            start_orbit_scale=1.0,
            final_orbit_scale=1.0,
            angle_range=-2.0 * math.pi,
            body_mass=1.0,
        )

    def body_orbit_spiral(self) -> None:
        self._setup_spiral(
            count=129,
            initial_speed=105.0,
            start_orbit_scale=0.750,
            final_orbit_scale=1.25,
            angle_range=-6.0 * math.pi,
            body_mass=1.0,
            sun_velocity=(-0.001, 0.0),
        )

    def party_over_spiral(self) -> None:
        self._setup_spiral(
            count=129,
            initial_speed=30.0,
            start_orbit_scale=0.125,
            final_orbit_scale=1.5,
            angle_range=-12.0 * math.pi,
            body_mass=0.25,
        )

    def body_circle(self) -> None:
        self._setup_spiral(
            count=129,
            initial_speed=125.0,
            start_orbit_scale=1.0,
            final_orbit_scale=1.0,
            angle_range=-6.0 * math.pi,
            body_mass=10.0,
        )

    def square_grid(self, rows: int = 15, columns: int = 10) -> None:
        self.bodies = []
        body_idx = 0
        for row in range(rows):
            unit_step_x = float(row) / float(rows - 1)
            for col in range(columns):
                unit_step_y = float(col) / float(columns - 1)
                grid_pos = pygame.math.Vector2(unit_step_x * self.width, unit_step_y * self.height)
                self.bodies.append(
                    SolarBody(
                        mass=1000.0,
                        radius=EARTH_RADIUS,
                        color=BODY_COLORS[body_idx % len(BODY_COLORS)],
                        position=grid_pos,
                        velocity=pygame.math.Vector2(0.0, 0.0),
                    )
                )
                body_idx += 1
        self.create_textures()

    def sun_dance(self) -> None:
        initial_speed = 350.0
        sun_pos = pygame.math.Vector2(self.width / 2.0, self.height / 2.0)
        to_body = rotate_vec(pygame.math.Vector2(EARTH_ORBIT_RADIUS, 0.0), 0.0)
        perp_unit = unit_vec(clockwise_perpendicular(to_body))

        self.bodies = [
            SolarBody(
                mass=1.0,
                radius=EARTH_RADIUS,
                color=(255, 255, 0),
                position=sun_pos,
                velocity=pygame.math.Vector2(0.0, 0.0),
            ),
            SolarBody(
                mass=700000.0,
                radius=EARTH_RADIUS,
                color=(0, 0, 255),
                position=sun_pos + to_body,
                velocity=initial_speed * perp_unit,
            ),
            SolarBody(
                mass=700000.0,
                radius=EARTH_RADIUS,
                color=(0, 255, 0),
                position=sun_pos + to_body + pygame.math.Vector2(100.0, 0.0),
                velocity=-initial_speed * perp_unit,
            ),
        ]
        self.create_textures()

    def draw_force_vectors(self, positions: List[pygame.math.Vector2]) -> None:
        force_scale = 0.5
        line_width = 2
        arrow_size = 8.0
        color = (0, 255, 0)

        for from_idx in range(len(self.bodies)):
            net_force = all_bodies_force(self.bodies, from_idx)
            force_magnitude = net_force.length()
            if force_magnitude < 0.01:
                continue

            force_normalized = net_force / force_magnitude
            force_vector = force_normalized * force_magnitude * force_scale

            start = positions[from_idx]
            end = start + force_vector

            pygame.draw.line(self.screen, color, start, end, line_width)

            perpendicular = pygame.math.Vector2(-force_normalized.y, force_normalized.x)
            p1 = end
            p2 = end - force_normalized * arrow_size + perpendicular * (arrow_size * 0.5)
            p3 = end - force_normalized * arrow_size - perpendicular * (arrow_size * 0.5)
            pygame.draw.polygon(self.screen, color, [p1, p2, p3])

    def handle_events(self) -> bool:
        for event in pygame.event.get():
            if event.type == pygame.QUIT:
                return False
            if event.type == pygame.KEYDOWN:
                if event.key == pygame.K_ESCAPE:
                    return False
                elif event.key == pygame.K_f:
                    self.show_forces = not self.show_forces
                elif event.key == pygame.K_SPACE:
                    self.actions[self.action_index]()
                elif event.key == pygame.K_RIGHT:
                    self.action_index = (self.action_index + 1) % len(self.actions)
                    self.actions[self.action_index]()
                elif event.key == pygame.K_LEFT:
                    self.action_index = (self.action_index - 1) % len(self.actions)
                    self.actions[self.action_index]()
        return True

    def draw(self, delta_time: float) -> None:
        t = min(delta_time, 0.020)
        n = len(self.bodies)

        accel: List[pygame.math.Vector2] = []
        for i in range(n):
            sum_force = all_bodies_force(self.bodies, i)
            accel.append((1.0 / self.bodies[i].mass) * sum_force)

        next_velocity: List[pygame.math.Vector2] = []
        next_position: List[pygame.math.Vector2] = []
        for i, body in enumerate(self.bodies):
            next_velocity.append(t * accel[i] + body.velocity)
            next_position.append((t * t / 2.0) * accel[i] + t * body.velocity + body.position)

        self.screen.fill((0, 0, 0))

        if self.show_forces:
            self.draw_force_vectors(next_position)

        for i, body in enumerate(self.bodies):
            if body.surface is not None:
                pos = next_position[i]
                top_left = (pos.x - body.radius, pos.y - body.radius)
                self.screen.blit(body.surface, top_left)

            body.position = next_position[i]
            body.velocity = next_velocity[i]

        y_offset = 8
        for text_surface in self.controls_surfaces:
            self.screen.blit(text_surface, (8, y_offset))
            y_offset += text_surface.get_height() + 2

        pygame.display.flip()

    def run(self) -> None:
        clock = pygame.time.Clock()
        running = True
        while running:
            delta_time = clock.tick() / 1000.0
            running = self.handle_events()
            self.draw(delta_time)

        pygame.quit()


if __name__ == "__main__":
    game = Game()
    game.run()
    sys.exit(0)
