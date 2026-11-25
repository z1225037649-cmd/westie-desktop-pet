"""Pose evaluation shared by authoring and preview, in normalized Blender axes."""
import math
import numpy as np

BONES = [
    ('Root', None, (0, 0, .12)),
    ('Pelvis', 'Root', (0, .28, .31)),
    ('Spine', 'Pelvis', (0, .02, .55)),
    ('Chest', 'Spine', (0, -.30, .81)),
    ('Neck', 'Chest', (0, -.36, 1.02)),
    ('Head', 'Neck', (0, -.36, 1.16)),
    ('Jaw', 'Head', (0, -.50, 1.28)),
    ('Ear.L', 'Head', (.27, -.37, 1.55)),
    ('Ear.R', 'Head', (-.27, -.37, 1.55)),
    ('Shoulder.L', 'Chest', (.17, -.31, .60)),
    ('Forearm.L', 'Shoulder.L', (.17, -.36, .31)),
    ('Paw.L', 'Forearm.L', (.17, -.40, .085)),
    ('Shoulder.R', 'Chest', (-.17, -.31, .60)),
    ('Forearm.R', 'Shoulder.R', (-.17, -.36, .31)),
    ('Paw.R', 'Forearm.R', (-.17, -.40, .085)),
    ('Hip.L', 'Pelvis', (.25, .26, .31)),
    ('Hock.L', 'Hip.L', (.25, .42, .13)),
    ('HindPaw.L', 'Hock.L', (.23, .49, .045)),
    ('Hip.R', 'Pelvis', (-.25, .26, .31)),
    ('Hock.R', 'Hip.R', (-.25, .42, .13)),
    ('HindPaw.R', 'Hock.R', (-.23, .49, .045)),
    ('TailRoot', 'Pelvis', (0, .54, .29)),
    ('TailMid', 'TailRoot', (0, .74, .47)),
    ('TailTip', 'TailMid', (0, .79, .65)),
]
INDEX = {entry[0]: index for index, entry in enumerate(BONES)}
PIVOTS = np.array([entry[2] for entry in BONES], dtype=float)
PARENTS = [INDEX[entry[1]] if entry[1] else -1 for entry in BONES]
AXIS = np.array([[1, 0, 0], [0, 0, 1], [0, -1, 0]], dtype=float)
SLEEP_STEPS = np.r_[np.arange(1, 8) / 8, .9375, .96875]
SLEEP_CURVATURE = .80
SHAPES = ['Blink.L', 'Blink.R', 'JawOpen', 'Smile', 'LieDownCorrective'] + ['LieDownTransition.%d' % index for index in range(1, len(SLEEP_STEPS) + 1)]
DURATIONS = {'Idle': 4, 'Blink': 1.2, 'Pet': 4, 'Eat': 5, 'PlayBall': 4, 'Sleep': 5, 'Wake': 4}


def smooth(a, b, values):
    t = np.clip((values - a) / (b - a), 0, 1)
    return t * t * (3 - 2 * t)


def rot(axis, angle):
    c, s = math.cos(angle), math.sin(angle)
    if axis == 0:
        return np.array([[1, 0, 0], [0, c, -s], [0, s, c]])
    if axis == 1:
        return np.array([[c, 0, s], [0, 1, 0], [-s, 0, c]])
    return np.array([[c, -s, 0], [s, c, 0], [0, 0, 1]])


def matrix_quaternion(matrix):
    # xyzw, normalized. All authored joints stay far from a 180-degree rotation.
    w = math.sqrt(max(0, 1 + np.trace(matrix))) / 2
    if w > 1e-6:
        q = np.array([(matrix[2, 1] - matrix[1, 2]) / (4 * w), (matrix[0, 2] - matrix[2, 0]) / (4 * w), (matrix[1, 0] - matrix[0, 1]) / (4 * w), w])
    else:
        raise ValueError('Unexpected half-turn pose.')
    return q / np.linalg.norm(q)


def hierarchy(rotations, offsets=None):
    offsets = {} if offsets is None else offsets
    world = []
    for index, (name, _, _) in enumerate(BONES):
        parent = PARENTS[index]
        local = np.eye(4)
        local[:3, :3] = rotations.get(name, np.eye(3))
        local[:3, 3] = PIVOTS[index] - (PIVOTS[parent] if parent >= 0 else 0) + offsets.get(name, 0)
        world.append(local if parent < 0 else world[parent] @ local)
    return np.array(world)


def limb_ik(rotations, side, target, offsets=None):
    shoulder = INDEX['Shoulder.' + side]
    forearm = INDEX['Forearm.' + side]
    paw = INDEX['Paw.' + side]
    parent = PARENTS[shoulder]
    world = hierarchy(rotations, offsets)
    desired = world[parent][:3, :3].T @ (np.array(target) - world[shoulder][:3, 3])
    first = PIVOTS[forearm] - PIVOTS[shoulder]
    second = PIVOTS[paw] - PIVOTS[forearm]
    l1, l2 = np.linalg.norm(first[1:]), np.linalg.norm(second[1:])
    dy, dz = desired[1:]
    distance = min(max(math.hypot(dy, dz), abs(l1 - l2) + 1e-7), l1 + l2 - 1e-8)
    theta = math.atan2(dz, dy)
    inner = math.acos(np.clip((l1 * l1 + distance * distance - l2 * l2) / (2 * l1 * distance), -1, 1))
    angle1 = theta + inner
    end = np.array([dy, dz]) * (distance / max(math.hypot(dy, dz), 1e-8))
    joint = np.array([math.cos(angle1), math.sin(angle1)]) * l1
    angle2 = math.atan2(*(end - joint)[::-1])
    shoulder_angle = angle1 - math.atan2(first[2], first[1])
    forearm_angle = angle2 - math.atan2(second[2], second[1]) - shoulder_angle
    rotations['Shoulder.' + side] = rot(0, shoulder_angle)
    rotations['Forearm.' + side] = rot(0, forearm_angle)
    # Keep each paw flat against the ground, or level during a ball reach.
    parent_angle = math.atan2(world[parent][2, 1], world[parent][1, 1])
    rotations['Paw.' + side] = rot(0, -parent_angle - shoulder_angle - forearm_angle)


def sleep_rotations(amount):
    result = {'Spine': rot(0, .60 * amount), 'Chest': rot(0, .04 * amount),
              'Neck': rot(0, -.02 * amount), 'Head': rot(0, .14 * amount)}
    for side, sign in [('L', 1), ('R', -1)]:
        rest = PIVOTS[INDEX['Paw.' + side]]
        target = rest * (1 - amount) + np.array([sign * .19, -.60, .090]) * amount
        limb_ik(result, side, target, {'Spine': np.array([0, 0, -.05 * amount])})
    return result


def sleep_head_offset(rotations, offsets, amount):
    if amount <= 0: return
    curvature = SLEEP_CURVATURE * amount
    point = PIVOTS[INDEX['Head']]
    height = .95
    angle = curvature * height
    target = np.array([point[0], point[1] * math.cos(angle) - (1 - math.cos(angle)) / curvature - (point[2] - 1.05) * math.sin(angle),
                       .10 + math.sin(angle) / curvature + point[1] * math.sin(angle) + (point[2] - 1.05) * math.cos(angle)])
    matrices = hierarchy(rotations, offsets)
    head, parent = INDEX['Head'], INDEX['Neck']
    offsets['Head'] = matrices[parent, :3, :3].T @ (target - matrices[head, :3, 3])


def pose(action, time):
    duration = DURATIONS[action]
    t = min(max(time, 0), duration)
    envelope = math.sin(math.pi * t / duration) ** 2
    rotations = {}
    offsets = {}
    morph = np.zeros(len(SHAPES))
    if action == 'Idle':
        rotations['Chest'] = rot(0, .008 * math.sin(2 * math.pi * t / duration))
        rotations['Head'] = rot(2, .045 * math.sin(2 * math.pi * t / duration))
        rotations['TailMid'] = rot(2, .075 * math.sin(4 * math.pi * t / duration))
        morph[:2] = max(0, 1 - abs(t - 2.4) / .15)
    elif action == 'Blink':
        morph[:2] = math.sin(math.pi * t / duration) ** 6
    elif action == 'Pet':
        rotations['Neck'] = rot(1, -.12 * envelope)
        rotations['Head'] = rot(0, -.12 * envelope) @ rot(1, .12 * envelope * math.sin(t * 2.2))
        rotations['Ear.L'] = rot(1, -.12 * envelope)
        rotations['Ear.R'] = rot(1, .12 * envelope)
        rotations['TailRoot'] = rot(2, .24 * envelope * math.sin(t * 6))
        morph[:2] = .85 * envelope
        morph[3] = .8 * envelope
    elif action == 'Eat':
        reach = float(smooth(.3, 1.3, t)) * (1 - float(smooth(3.7, 4.7, t)))
        rotations['Neck'] = rot(0, .13 * reach)
        rotations['Head'] = rot(0, .10 * reach)
        chew = .5 + .5 * math.sin((t - 1.3) * math.pi * 5)
        morph[2] = reach * (.25 + .75 * chew)
        morph[3] = .15 * reach
        rotations['Jaw'] = rot(0, .07 * morph[2])
    elif action == 'PlayBall':
        reach = float(smooth(.45, 1.5, t)) * (1 - float(smooth(2.2, 3.3, t)))
        rest = PIVOTS[INDEX['Paw.L']]
        target = rest * (1 - reach) + np.array([.17, -.62, .17]) * reach
        limb_ik(rotations, 'L', target)
        rotations['Head'] = rot(0, .08 * reach) @ rot(2, -.05 * reach)
        rotations['TailRoot'] = rot(2, .16 * envelope * math.sin(5 * t))
    elif action == 'Sleep':
        amount = float(smooth(.4, 3.6, t))
        rotations = sleep_rotations(amount)
        offsets['Spine'] = np.array([0, 0, -.05 * amount])
        sleep_head_offset(rotations, offsets, amount)
        morph[:2] = float(smooth(.3, 2.5, t))
        morph[4] = amount
        knots = np.r_[0., SLEEP_STEPS, 1.]
        for index, knot in enumerate(SLEEP_STEPS):
            morph[5 + index] = max(0, min((amount - knots[index]) / (knot - knots[index]), (knots[index + 2] - amount) / (knots[index + 2] - knot)))
    elif action == 'Wake':
        amount = 1 - float(smooth(.3, 3.2, t))
        rotations = sleep_rotations(amount)
        offsets['Spine'] = np.array([0, 0, -.05 * amount])
        sleep_head_offset(rotations, offsets, amount)
        morph[:2] = 1 - float(smooth(.8, 1.8, t))
        morph[2] = .65 * math.sin(math.pi * float(smooth(.5, 2.5, t))) ** 4
        morph[4] = amount
        knots = np.r_[0., SLEEP_STEPS, 1.]
        for index, knot in enumerate(SLEEP_STEPS):
            morph[5 + index] = max(0, min((amount - knots[index]) / (knot - knots[index]), (knots[index + 2] - amount) / (knots[index + 2] - knot)))
        rotations['Ear.L'] = rot(1, -.09 * math.sin(math.pi * t / duration))
        rotations['Ear.R'] = rot(1, .09 * math.sin(math.pi * t / duration))
    return rotations, offsets, morph


def skin(points, joints, weights, matrices, targets=None, morph=None):
    p = points.copy()
    if targets is not None and morph is not None:
        p += np.einsum('k,kni->ni', morph, targets)
    output = np.zeros_like(p)
    for slot in range(4):
        selected = matrices[joints[:, slot]].copy()
        selected[:, :3, 3] -= np.einsum('nij,nj->ni', selected[:, :3, :3], PIVOTS[joints[:, slot]])
        transformed = np.einsum('nij,nj->ni', selected[:, :3, :3], p) + selected[:, :3, 3]
        output += transformed * weights[:, slot:slot + 1]
    return output
