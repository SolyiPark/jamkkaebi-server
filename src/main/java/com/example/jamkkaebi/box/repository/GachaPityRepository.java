package com.example.jamkkaebi.box.repository;

import com.example.jamkkaebi.box.domain.GachaPity;
import org.springframework.data.jpa.repository.JpaRepository;

import java.util.List;

public interface GachaPityRepository extends JpaRepository<GachaPity, Long> {

    List<GachaPity> findAllByUserId(Long userId);
}
