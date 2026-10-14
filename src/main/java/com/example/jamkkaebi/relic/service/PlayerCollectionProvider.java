package com.example.jamkkaebi.relic.service;

import com.example.jamkkaebi.friend.spi.PlayerCollection;
import com.example.jamkkaebi.friend.spi.PlayerCollectionReader;
import com.example.jamkkaebi.relic.domain.Era;
import com.example.jamkkaebi.relic.repository.EraCompletion;
import com.example.jamkkaebi.relic.repository.UserRelicRepository;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.Collection;
import java.util.EnumMap;
import java.util.HashMap;
import java.util.Map;

/**
 * 친구 카드의 수집률·시대별 복원 수를 채운다.
 * (친구 도메인의 {@link PlayerCollectionReader} 의 구현)
 */
@Service
public class PlayerCollectionProvider implements PlayerCollectionReader {

    private final UserRelicRepository userRelicRepository;

    public PlayerCollectionProvider(UserRelicRepository userRelicRepository) {
        this.userRelicRepository = userRelicRepository;
    }

    @Override
    @Transactional(readOnly = true)
    public Map<Long, PlayerCollection> readAll(Collection<Long> userIds) {
        if (userIds.isEmpty()) {
            return Map.of();
        }
        Map<Long, Map<Era, Integer>> byUser = new HashMap<>();
        for (EraCompletion completion : userRelicRepository.countCompletedByEra(userIds)) {
            byUser.computeIfAbsent(completion.userId(), id -> new EnumMap<>(Era.class))
                    .merge(completion.era(), (int) completion.count(), Integer::sum);
        }
        Map<Long, PlayerCollection> collections = new HashMap<>();
        byUser.forEach((userId, counts) -> collections.put(userId, new PlayerCollection(counts)));
        return collections;
    }
}
