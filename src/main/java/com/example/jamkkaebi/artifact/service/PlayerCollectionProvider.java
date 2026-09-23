package com.example.jamkkaebi.artifact.service;

import com.example.jamkkaebi.artifact.domain.Era;
import com.example.jamkkaebi.artifact.repository.EraCompletion;
import com.example.jamkkaebi.artifact.repository.UserArtifactRepository;
import com.example.jamkkaebi.friend.spi.PlayerCollection;
import com.example.jamkkaebi.friend.spi.PlayerCollectionReader;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.Collection;
import java.util.EnumMap;
import java.util.HashMap;
import java.util.Map;

/**
 * 친구 카드의 수집률·시대별 복원 수를 채운다. (친구 도메인의 {@link PlayerCollectionReader} 의 구현)
 *
 * <p>수집률을 저장하지 않는 이유는 {@code status = COMPLETED} 를 세면 되기 때문이다.
 * 건물은 세지 않는다 — 유물 3종을 채우면 자동으로 지급되므로 이중 계산이 된다.
 */
@Service
public class PlayerCollectionProvider implements PlayerCollectionReader {

    private final UserArtifactRepository userArtifactRepository;

    public PlayerCollectionProvider(UserArtifactRepository userArtifactRepository) {
        this.userArtifactRepository = userArtifactRepository;
    }

    @Override
    @Transactional(readOnly = true)
    public Map<Long, PlayerCollection> readAll(Collection<Long> userIds) {
        if (userIds.isEmpty()) {
            return Map.of();
        }
        Map<Long, Map<Era, Integer>> byUser = new HashMap<>();
        for (EraCompletion completion : userArtifactRepository.countCompletedByEra(userIds)) {
            byUser.computeIfAbsent(completion.userId(), id -> new EnumMap<>(Era.class))
                    .merge(completion.era(), (int) completion.count(), Integer::sum);
        }
        Map<Long, PlayerCollection> collections = new HashMap<>();
        byUser.forEach((userId, counts) -> collections.put(userId, new PlayerCollection(counts)));
        return collections;
    }
}
