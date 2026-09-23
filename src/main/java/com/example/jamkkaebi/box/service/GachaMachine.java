package com.example.jamkkaebi.box.service;

import com.example.jamkkaebi.box.config.BoxProperties;
import com.example.jamkkaebi.box.domain.GachaPity;
import com.example.jamkkaebi.box.repository.GachaPityRepository;
import org.springframework.stereotype.Component;

import java.util.ArrayList;
import java.util.Collection;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ThreadLocalRandom;

/**
 * 상자에서 유물을 뽑는다.
 */
@Component
public class GachaMachine {

    private final GachaPityRepository pityRepository;
    private final BoxProperties properties;

    public GachaMachine(GachaPityRepository pityRepository, BoxProperties properties) {
        this.pityRepository = pityRepository;
        this.properties = properties;
    }

    /**
     * 한 장 뽑고 천장 카운터를 갱신한다.
     *
     * @param userId      뽑는 사람
     * @param allIds      모든 유물 마스터 번호
     * @param ownedIds    이미 가진 유물 번호
     * @return 뽑힌 유물 번호
     */
    public Integer draw(Long userId, List<Integer> allIds, Collection<Integer> ownedIds) {
        Set<Integer> owned = new HashSet<>(ownedIds);
        List<Integer> unowned = allIds.stream().filter(id -> !owned.contains(id)).toList();
        List<Integer> duplicates = allIds.stream().filter(owned::contains).toList();

        Map<Integer, GachaPity> pity = loadPity(userId, unowned);
        Integer drawn = pick(unowned, duplicates, pity);
        updatePity(pity, unowned, drawn);
        return drawn;
    }

    private Integer pick(List<Integer> unowned, List<Integer> duplicates, Map<Integer, GachaPity> pity) {
        // 천장에 걸린 유물이 있으면 그것부터
        Integer guaranteed = unowned.stream()
                .filter(id -> pity.get(id).reached(properties.pityThreshold()))
                .max((left, right) -> Integer.compare(
                        pity.get(left).getMissStreak(), pity.get(right).getMissStreak()))
                .orElse(null);
        if (guaranteed != null) {
            return guaranteed;
        }

        if (unowned.isEmpty()) {
            return randomOf(duplicates);
        }
        if (duplicates.isEmpty()) {
            return randomOf(unowned);
        }

        int total = properties.newCardWeight() + properties.duplicateWeight();
        boolean pickNew = ThreadLocalRandom.current().nextInt(total) < properties.newCardWeight();
        return randomOf(pickNew ? unowned : duplicates);
    }

    // 미획득 유물의 천장 카운터를 수정한다. 뽑힌 것은 0으로 돌아가고 나머지는 하나씩 오른다.
    private void updatePity(Map<Integer, GachaPity> pity, List<Integer> unowned, Integer drawn) {
        for (Integer id : unowned) {
            GachaPity counter = pity.get(id);
            if (id.equals(drawn)) {
                counter.hit();
            } else {
                counter.miss();
            }
        }
    }

    private Map<Integer, GachaPity> loadPity(Long userId, List<Integer> unowned) {
        Map<Integer, GachaPity> stored = new HashMap<>();
        pityRepository.findAllByUserId(userId)
                .forEach(counter -> stored.put(counter.getArtifactId(), counter));

        List<GachaPity> created = new ArrayList<>();
        for (Integer id : unowned) {
            if (!stored.containsKey(id)) {
                created.add(GachaPity.start(userId, id));
            }
        }
        pityRepository.saveAll(created).forEach(counter -> stored.put(counter.getArtifactId(), counter));
        return stored;
    }

    private static Integer randomOf(List<Integer> candidates) {
        return candidates.get(ThreadLocalRandom.current().nextInt(candidates.size()));
    }
}
